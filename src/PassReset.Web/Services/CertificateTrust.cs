using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PassReset.Web.Services;

/// <summary>
/// Pure (no I/O) certificate-trust decision helper. Extracted for testability — the SMTP
/// callback in <see cref="SmtpEmailService"/> delegates here.
/// </summary>
/// <remarks>
/// NEVER add an unconditional <c>return true</c>. Any bypass MUST go through an explicit
/// thumbprint allowlist populated from configuration.
/// </remarks>
public static class CertificateTrust
{
    /// <summary>
    /// Decides whether a presented server certificate should be trusted.
    /// Happy path: returns true when the OS chain validation reported no errors.
    /// Escape hatch: when chain errors are present, returns true iff the leaf cert's
    /// SHA-1 or SHA-256 thumbprint matches an entry in <paramref name="allowedThumbprints"/>.
    /// </summary>
    /// <param name="cert">Leaf certificate presented by the remote server. Null returns false.</param>
    /// <param name="chain">Certificate chain (unused; reserved for future diagnostics).</param>
    /// <param name="errors">SslPolicyErrors reported by the OS chain validator.</param>
    /// <param name="allowedThumbprints">Configured thumbprint allowlist (SHA-1 40 hex or SHA-256 64 hex). Null/empty = no allowlist.</param>
    public static bool IsTrusted(
        X509Certificate? cert,
        X509Chain? chain,
        SslPolicyErrors errors,
        IReadOnlyCollection<string>? allowedThumbprints)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (cert is null) return false;
        if (allowedThumbprints is null || allowedThumbprints.Count == 0) return false;

        var c2 = cert as X509Certificate2 ?? new X509Certificate2(cert);
        var thumbSha1 = c2.Thumbprint; // SHA-1 upper hex, no spaces

        string? thumbSha256 = null;

        foreach (var allowed in allowedThumbprints)
        {
            if (string.IsNullOrWhiteSpace(allowed)) continue;
            var normalized = allowed.Replace(" ", "").Replace(":", "");

            if (string.Equals(normalized, thumbSha1, StringComparison.OrdinalIgnoreCase))
                return true;

            // Also support SHA-256: compute and compare if allowlist entry is 64 hex chars
            if (normalized.Length == 64)
            {
                thumbSha256 ??= Convert.ToHexString(SHA256.HashData(c2.RawData));
                if (string.Equals(normalized, thumbSha256, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }
}
