using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;
using Xunit;

namespace PassReset.Tests.Services;

public class LdapTcpProbeTests
{
    private static LdapTcpProbe Build(PasswordChangeOptions opts) =>
        new(Options.Create(opts), NullLogger<LdapTcpProbe>.Instance);

    [Fact]
    public async Task CheckAsync_NoHostnamesConfigured_ReturnsNotConfigured()
    {
        var probe = Build(new PasswordChangeOptions { LdapHostnames = Array.Empty<string>() });

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnreachableHost_ReturnsUnhealthy()
    {
        // RFC 5737 TEST-NET-1: guaranteed unroutable. Probe should time out fast.
        var probe = Build(new PasswordChangeOptions
        {
            LdapHostnames = new[] { "192.0.2.1" },
            LdapPort      = 636,
        });

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckAsync_IgnoresBlankHostnames()
    {
        // Whitespace-only entries in LdapHostnames are skipped, not probed.
        var probe = Build(new PasswordChangeOptions
        {
            LdapHostnames = new[] { "", "  " },
            LdapPort      = 636,
        });

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.NotConfigured, result.Status);
    }
}
