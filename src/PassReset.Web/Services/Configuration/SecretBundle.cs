namespace PassReset.Web.Services.Configuration;

/// <summary>
/// In-memory representation of the four operator-managed secrets. A null property means
/// "not set" — distinct from empty string. Used as the payload of <see cref="ISecretStore"/>.
/// </summary>
public sealed record SecretBundle(
    string? LdapPassword,
    string? ServiceAccountPassword,
    string? SmtpPassword,
    string? RecaptchaPrivateKey)
{
    /// <summary>An empty bundle with all four values null. Used when <c>secrets.dat</c> does not exist.</summary>
    public static SecretBundle Empty { get; } = new(null, null, null, null);
}
