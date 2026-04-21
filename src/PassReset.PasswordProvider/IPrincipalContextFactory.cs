using System.DirectoryServices.AccountManagement;

namespace PassReset.PasswordProvider;

/// <summary>
/// Seam abstracting <see cref="PrincipalContext"/> + <see cref="UserPrincipal.FindByIdentity(PrincipalContext, string)"/>
/// so that contract tests can inject a fake. Scope is deliberately minimal —
/// mirrors the two AD operations <see cref="PasswordChangeProvider"/> actually
/// performs. Not a general-purpose `System.DirectoryServices` wrapper.
/// </summary>
public interface IPrincipalContextFactory
{
    /// <summary>
    /// Creates a <see cref="PrincipalContext"/> in <see cref="ContextType.Domain"/> mode.
    /// The caller is responsible for disposing the returned context.
    /// </summary>
    /// <param name="container">Optional OU/container DN (null = domain root).</param>
    /// <param name="username">Optional explicit bind username; null = automatic/current-user context.</param>
    /// <param name="password">Optional explicit bind password; required when username is non-null.</param>
    PrincipalContext CreateDomainContext(string? container = null, string? username = null, string? password = null);

    /// <summary>
    /// Resolves a user by <paramref name="identityType"/> + <paramref name="identityValue"/>.
    /// Returns null when no match. Caller is responsible for disposing the returned principal.
    /// </summary>
    UserPrincipal? FindUser(PrincipalContext context, IdentityType identityType, string identityValue);
}
