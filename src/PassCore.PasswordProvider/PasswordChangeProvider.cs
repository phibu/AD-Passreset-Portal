using PassCore.Common;

namespace PassCore.PasswordProvider;

/// <summary>
/// Stub implementation of <see cref="IPasswordChangeProvider"/> for the Windows AD password change provider.
/// Full implementation is wired in T02 / S02.
/// </summary>
public class PasswordChangeProvider : IPasswordChangeProvider
{
    /// <inheritdoc />
    public ApiErrorItem? PerformPasswordChange(string username, string currentPassword, string newPassword)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public string? GetUserEmail(string username)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public TimeSpan GetDomainMaxPasswordAge()
        => throw new NotImplementedException();
}
