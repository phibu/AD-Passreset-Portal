using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PassReset.Web.Middleware;

/// <summary>
/// Belt-and-braces guard for the admin UI. <see cref="Program"/> uses <c>MapWhen</c>
/// to route admin requests only from the loopback listener; this middleware additionally
/// verifies <see cref="HttpContext.Connection.RemoteIpAddress"/> is loopback and returns
/// 404 otherwise. Defense against a future refactor accidentally exposing <c>/admin</c>.
/// </summary>
internal sealed class LoopbackOnlyGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoopbackOnlyGuardMiddleware> _log;

    public LoopbackOnlyGuardMiddleware(RequestDelegate next, ILogger<LoopbackOnlyGuardMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !System.Net.IPAddress.IsLoopback(remote))
        {
            _log.LogWarning("Admin UI request from non-loopback address {Remote} blocked", remote);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await _next(context);
    }
}
