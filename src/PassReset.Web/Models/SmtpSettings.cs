namespace PassReset.Web.Models;

/// <summary>
/// Configuration for the SMTP relay used to send email notifications.
/// Supports Mimecast and standard SMTP relays on port 587 with STARTTLS.
/// </summary>
public class SmtpSettings
{
    /// <summary>SMTP relay hostname, e.g. smtp-relay.mimecast.com</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port. Default 587 for STARTTLS.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Enable SSL/TLS. Set true for port 587 (STARTTLS) or 465 (SMTPS).</summary>
    public bool UseSsl { get; set; } = true;

    /// <summary>SMTP authentication username. Leave empty for anonymous relay.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP authentication password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>From address shown in outbound emails.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Display name shown in outbound emails.</summary>
    public string FromName { get; set; } = "PassReset Self-Service";

    /// <summary>
    /// Optional SHA-1 or SHA-256 thumbprints (hex, case-insensitive, spaces tolerated) of SMTP server certificates
    /// explicitly trusted even when the chain cannot be validated against the system store.
    /// Use this for internal-CA relays where installing the root into LocalMachine\Root is not feasible.
    /// Default (empty/null): rely solely on the OS trust store.
    /// </summary>
    public string[]? TrustedCertificateThumbprints { get; set; }
}
