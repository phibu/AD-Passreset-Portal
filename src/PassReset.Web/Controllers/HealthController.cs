using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PassReset.PasswordProvider;

namespace PassReset.Web.Controllers;

/// <summary>
/// Provides a health probe for load balancers and monitoring.
/// GET /api/health — checks AD connectivity when not using the debug provider.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IOptions<PasswordChangeOptions> options,
        ILogger<HealthController> logger)
    {
        _options = options;
        _logger  = logger;
    }

    /// <summary>Returns the application health status including AD connectivity.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Get()
    {
        var adStatus = CheckAdConnectivity();

        var result = new
        {
            status    = adStatus ? "healthy" : "degraded",
            timestamp = DateTimeOffset.UtcNow,
            checks    = new { ad = adStatus ? "ok" : "unreachable" },
        };

        return adStatus ? Ok(result) : StatusCode(503, result);
    }

    private bool CheckAdConnectivity()
    {
        var opts = _options.Value;

        // When using automatic context, verify the machine is domain-joined.
        if (opts.UseAutomaticContext)
        {
            try
            {
                using var ctx = new System.DirectoryServices.AccountManagement.PrincipalContext(
                    System.DirectoryServices.AccountManagement.ContextType.Domain);
                return ctx.ConnectedServer != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AD health check failed (automatic context)");
                return false;
            }
        }

        // When using explicit credentials, verify the LDAP endpoint is reachable.
        if (opts.LdapHostnames.Length == 0 || string.IsNullOrWhiteSpace(opts.LdapHostnames[0]))
            return true; // No LDAP configured — skip check (debug provider scenario)

        try
        {
            var host = opts.LdapHostnames[0];
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect(host, opts.LdapPort);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD health check failed (LDAP endpoint {Host}:{Port})",
                opts.LdapHostnames[0], opts.LdapPort);
            return false;
        }
    }
}
