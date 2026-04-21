using System.DirectoryServices.Protocols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Cross-platform <see cref="IPasswordChangeProvider"/> backed by
/// <see cref="System.DirectoryServices.Protocols.LdapConnection"/>. Runs on Windows, Linux, and macOS.
/// Behavioral parity with the Windows provider is enforced by the shared
/// <c>IPasswordChangeProviderContract</c> test suite.
/// </summary>
public sealed class LdapPasswordChangeProvider : IPasswordChangeProvider
{
    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<LdapPasswordChangeProvider> _logger;
    private readonly Func<ILdapSession> _sessionFactory;

    public LdapPasswordChangeProvider(
        IOptions<PasswordChangeOptions> options,
        ILogger<LdapPasswordChangeProvider> logger,
        Func<ILdapSession> sessionFactory)
    {
        _options = options;
        _logger = logger;
        _sessionFactory = sessionFactory;

        if (OperatingSystem.IsWindows())
        {
            _logger.LogInformation(
                "LdapPasswordChangeProvider active on Windows (ProviderMode={Mode}). " +
                "UserCannotChangePassword ACE check is Linux-deferred; AD server-side enforcement applies.",
                _options.Value.ProviderMode);
        }
    }

    /// <summary>
    /// Resolves <paramref name="username"/> to its distinguished name by searching each
    /// attribute in <see cref="PasswordChangeOptions.AllowedUsernameAttributes"/> in order.
    /// Returns null when no attribute matches.
    /// </summary>
    internal async Task<string?> FindUserDnAsync(ILdapSession session, string username)
    {
        await Task.Yield();  // reserved for future async LDAP APIs
        var opts = _options.Value;
        foreach (var attr in opts.AllowedUsernameAttributes)
        {
            var ldapAttr = attr.ToLowerInvariant() switch
            {
                "samaccountname"    => LdapAttributeNames.SamAccountName,
                "userprincipalname" => LdapAttributeNames.UserPrincipalName,
                "mail"              => LdapAttributeNames.Mail,
                _ => null,
            };
            if (ldapAttr is null)
            {
                _logger.LogWarning("Ignoring unknown AllowedUsernameAttributes entry: {Attr}", attr);
                continue;
            }

            var filter = $"({ldapAttr}={EscapeLdapFilterValue(username)})";
            var request = new SearchRequest(
                distinguishedName: opts.BaseDn,
                ldapFilter: filter,
                searchScope: SearchScope.Subtree,
                attributeList: new[] { LdapAttributeNames.DistinguishedName });
            var response = session.Search(request);

            if (response.Entries.Count == 1)
                return response.Entries[0].DistinguishedName;

            if (response.Entries.Count > 1)
            {
                _logger.LogWarning(
                    "Ambiguous match: {Count} entries for {Attr}={Username}. Treating as not found.",
                    response.Entries.Count, ldapAttr, username);
            }
        }
        return null;
    }

    /// <summary>
    /// RFC 4515 LDAP filter value escaping: backslash, asterisk, parenthesis, NUL.
    /// Prevents filter injection when user input is interpolated into a search filter.
    /// </summary>
    internal static string EscapeLdapFilterValue(string value) =>
        value
            .Replace("\\", @"\5c")
            .Replace("*",  @"\2a")
            .Replace("(",  @"\28")
            .Replace(")",  @"\29")
            .Replace("\0", @"\00");

    public async Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
    {
        using var session = _sessionFactory();

        try
        {
            session.Bind();
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP bind failed as service account");
            return new ApiErrorItem(ApiErrorCode.Generic,
                "Directory bind failed; contact your administrator.");
        }

        var userDn = await FindUserDnAsync(session, username);
        if (userDn is null)
        {
            _logger.LogInformation("User not found: {Username}", username);
            return new ApiErrorItem(ApiErrorCode.UserNotFound,
                "User not found in directory.") { FieldName = nameof(username) };
        }

        var opts = _options.Value;
        try
        {
            var modifyRequest = BuildChangePasswordRequest(userDn, currentPassword, newPassword, opts.AllowSetPasswordFallback);
            var response = session.Modify(modifyRequest);

            if (response.ResultCode != ResultCode.Success)
            {
                var extended = LdapErrorMapping.ExtractExtendedError(response.ErrorMessage);
                var mapped = LdapErrorMapping.Map(response.ResultCode, extended);
                _logger.LogWarning(
                    "ModifyResponse rejected: ResultCode={ResultCode} extendedError=0x{Extended:X8} mapped={Mapped}",
                    response.ResultCode, extended, mapped);
                return new ApiErrorItem(mapped, MapperMessageFor(mapped));
            }

            return null;
        }
        catch (DirectoryOperationException ex)
        {
            var extended = LdapErrorMapping.ExtractExtendedError(ex.Response?.ErrorMessage);
            var mapped = LdapErrorMapping.Map(ex.Response?.ResultCode ?? ResultCode.OperationsError, extended);
            _logger.LogWarning(ex,
                "DirectoryOperationException on Modify: ResultCode={ResultCode} extendedError=0x{Extended:X8} mapped={Mapped}",
                ex.Response?.ResultCode, extended, mapped);
            return new ApiErrorItem(mapped, MapperMessageFor(mapped));
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "Unexpected LDAP exception on password change");
            return new ApiErrorItem(ApiErrorCode.Generic, "Unexpected directory error.");
        }
    }

    private static ModifyRequest BuildChangePasswordRequest(
        string userDn, string current, string next, bool allowSetFallback)
    {
        // AD atomic change-password pattern: single ModifyRequest with Delete(old) + Add(new)
        // on unicodePwd. The value must be UTF-16LE-encoded and wrapped in literal quote chars.
        var oldBytes = System.Text.Encoding.Unicode.GetBytes($"\"{current}\"");
        var newBytes = System.Text.Encoding.Unicode.GetBytes($"\"{next}\"");

        if (allowSetFallback)
        {
            // Replace semantic: SetPassword equivalent — bypasses history. Opt-in only.
            var replace = new DirectoryAttributeModification
            {
                Operation = DirectoryAttributeOperation.Replace,
                Name = LdapAttributeNames.UnicodePwd,
            };
            replace.Add(newBytes);
            return new ModifyRequest(userDn, replace);
        }

        var del = new DirectoryAttributeModification
        {
            Operation = DirectoryAttributeOperation.Delete,
            Name = LdapAttributeNames.UnicodePwd,
        };
        del.Add(oldBytes);
        var add = new DirectoryAttributeModification
        {
            Operation = DirectoryAttributeOperation.Add,
            Name = LdapAttributeNames.UnicodePwd,
        };
        add.Add(newBytes);
        return new ModifyRequest(userDn, del, add);
    }

    private static string MapperMessageFor(ApiErrorCode code) => code switch
    {
        ApiErrorCode.InvalidCredentials          => "Current password is incorrect.",
        ApiErrorCode.UserNotFound                => "User not found in directory.",
        ApiErrorCode.ChangeNotPermitted          => "Password change is not permitted for this account.",
        ApiErrorCode.ComplexPassword             => "The new password does not meet domain complexity requirements.",
        ApiErrorCode.PortalLockout               => "Account is locked out. Contact your administrator.",
        ApiErrorCode.PasswordTooRecentlyChanged  => "Password was changed too recently; please wait before trying again.",
        _                                        => "Unexpected error.",
    };

    public string? GetUserEmail(string username)
        => throw new NotImplementedException();

    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
        => throw new NotImplementedException();

    public TimeSpan GetDomainMaxPasswordAge()
        => throw new NotImplementedException();

    public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync()
        => throw new NotImplementedException();
}
