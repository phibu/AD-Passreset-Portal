using PassReset.Common;

namespace PassReset.Web.Services.Configuration;

/// <summary>
/// The subset of <c>appsettings.Production.json</c> exposed through the admin UI.
/// Unmanaged keys (Logging, AllowedHosts, WebSettings, site-local additions) are
/// intentionally absent — they pass through <see cref="IAppSettingsEditor"/> untouched.
/// </summary>
public sealed record AppSettingsSnapshot(
    PasswordChangeSection PasswordChange,
    SmtpSection Smtp,
    RecaptchaPublicSection Recaptcha,
    SiemSyslogSection Siem,
    GroupsSection Groups,
    LocalPolicySection LocalPolicy);

public sealed record PasswordChangeSection(
    bool UseAutomaticContext,
    ProviderMode ProviderMode,
    string[] LdapHostnames,
    int LdapPort,
    bool LdapUseSsl,
    string BaseDn,
    string ServiceAccountDn,
    string DefaultDomain);

public sealed record GroupsSection(
    string[] AllowedAdGroups,
    string[] RestrictedAdGroups);

public sealed record SmtpSection(
    string Host,
    int Port,
    string Username,
    string FromAddress,
    bool UseStartTls);

public sealed record RecaptchaPublicSection(
    bool Enabled,
    string SiteKey);

public sealed record SiemSyslogSection(
    bool Enabled,
    string Host,
    int Port,
    string Protocol);

public sealed record LocalPolicySection(
    string? BannedWordsPath,
    string? LocalPwnedPasswordsPath,
    int MinBannedTermLength);
