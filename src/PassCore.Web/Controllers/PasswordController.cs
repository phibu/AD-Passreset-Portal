using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PassCore.Common;
using PassCore.Web.Models;

namespace PassCore.Web.Controllers;

/// <summary>
/// Handles password change requests.
/// Rate-limited to 5 requests per 5 minutes per client (see Program.cs).
/// Full implementation follows in a later task — this stub wires the rate-limit
/// policy and model binding so the middleware chain is exercisable end-to-end.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class PasswordController : ControllerBase
{
    private readonly IPasswordChangeProvider _provider;
    private readonly ILogger<PasswordController> _logger;

    public PasswordController(
        IPasswordChangeProvider provider,
        ILogger<PasswordController> logger)
    {
        _provider = provider;
        _logger   = logger;
    }

    /// <summary>
    /// Changes the password for the specified user account.
    /// POST /api/password
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("password-fixed-window")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Post([FromBody] ChangePasswordModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        _logger.LogInformation("Password change requested for user={User}", model.Username);

        var error = _provider.PerformPasswordChange(
            model.Username,
            model.CurrentPassword,
            model.NewPassword);

        if (error is not null)
        {
            _logger.LogWarning(
                "Password change failed for user={User} errorCode={Code}",
                model.Username, error.ErrorCode);
            return BadRequest(new { error });
        }

        _logger.LogInformation("Password change succeeded for user={User}", model.Username);
        return Ok(new { message = "Password changed successfully." });
    }
}
