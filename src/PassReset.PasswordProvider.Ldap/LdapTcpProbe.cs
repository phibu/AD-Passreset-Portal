using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Cross-platform AD connectivity probe. Attempts a TCP connect to each
/// configured <see cref="PasswordChangeOptions.LdapHostnames"/> entry on
/// <see cref="PasswordChangeOptions.LdapPort"/>. Returns <see cref="AdProbeStatus.Healthy"/>
/// as soon as any DC answers. Logic mirrors the pre-Phase-11-hygiene inline code
/// that lived in <c>HealthController.CheckAdConnectivityAsync</c>.
/// </summary>
public sealed class LdapTcpProbe : IAdConnectivityProbe
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<LdapTcpProbe> _logger;

    public LdapTcpProbe(IOptions<PasswordChangeOptions> options, ILogger<LdapTcpProbe> logger)
    {
        _options = options;
        _logger  = logger;
    }

    public async Task<AdProbeResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var sw   = Stopwatch.StartNew();

        var hostnames = (opts.LdapHostnames ?? Array.Empty<string>())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToArray();

        if (hostnames.Length == 0)
            return new AdProbeResult(AdProbeStatus.NotConfigured, sw.ElapsedMilliseconds);

        foreach (var host in hostnames)
        {
            try
            {
                using var cts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ConnectTimeout);
                using var client = new TcpClient();
                await client.ConnectAsync(host, opts.LdapPort, cts.Token);
                return new AdProbeResult(AdProbeStatus.Healthy, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AD connectivity probe failed for LDAP endpoint {Host}:{Port}",
                    host, opts.LdapPort);
            }
        }

        _logger.LogError(
            "AD connectivity probe failed — no LDAP endpoints reachable ({Hosts})",
            string.Join(", ", hostnames));
        return new AdProbeResult(AdProbeStatus.Unhealthy, sw.ElapsedMilliseconds);
    }
}
