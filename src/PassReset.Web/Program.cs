using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Web.Helpers;
using PassReset.Web.Models;
using PassReset.Web.Services;
using Serilog;
// NoOpEmailService lives in PassReset.Web.Helpers (development no-op).
// SmtpEmailService and PasswordExpiryNotificationService live in PassReset.Web.Services.

// Bootstrap logger captures startup failures before appsettings-driven config is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting PassReset");

    var builder = WebApplication.CreateBuilder(args);

    // ─── Serilog (reads Serilog section from appsettings; enrich with HTTP context) ─
    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ─── Configuration ────────────────────────────────────────────────────────────
    builder.Services.Configure<ClientSettings>(
        builder.Configuration.GetSection(nameof(ClientSettings)));
    builder.Services.Configure<WebSettings>(
        builder.Configuration.GetSection(nameof(WebSettings)));
    builder.Services.Configure<SmtpSettings>(
        builder.Configuration.GetSection(nameof(SmtpSettings)));
    builder.Services.Configure<EmailNotificationSettings>(
        builder.Configuration.GetSection(nameof(EmailNotificationSettings)));
    builder.Services.Configure<PasswordExpiryNotificationSettings>(
        builder.Configuration.GetSection(nameof(PasswordExpiryNotificationSettings)));
    builder.Services.Configure<SiemSettings>(
        builder.Configuration.GetSection(nameof(SiemSettings)));
    builder.Services.Configure<PasswordChangeOptions>(
        builder.Configuration.GetSection(nameof(PasswordChangeOptions)));
    builder.Services.AddSingleton<IValidateOptions<PasswordChangeOptions>, PasswordChangeOptionsValidator>();

    // ─── Provider registration (runtime config flag, no compile-time conditionals) ─
    var webSettings = builder.Configuration
        .GetSection(nameof(WebSettings))
        .Get<WebSettings>() ?? new WebSettings();

    var expirySettings = builder.Configuration
        .GetSection(nameof(PasswordExpiryNotificationSettings))
        .Get<PasswordExpiryNotificationSettings>() ?? new PasswordExpiryNotificationSettings();

    // ─── Startup configuration validation — abort on Error, warn on Warning ──
    var smtpSettings = builder.Configuration
        .GetSection(nameof(SmtpSettings))
        .Get<SmtpSettings>() ?? new SmtpSettings();
    var emailNotifSettings = builder.Configuration
        .GetSection(nameof(EmailNotificationSettings))
        .Get<EmailNotificationSettings>() ?? new EmailNotificationSettings();
    var clientSettings = builder.Configuration
        .GetSection(nameof(ClientSettings))
        .Get<ClientSettings>() ?? new ClientSettings();
    var passwordChangeOptions = builder.Configuration
        .GetSection(nameof(PasswordChangeOptions))
        .Get<PasswordChangeOptions>() ?? new PasswordChangeOptions();

    // ERROR — abort startup
    if (webSettings.UseDebugProvider && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "WebSettings.UseDebugProvider is true but Environment is not 'Development'. " +
            "Set UseDebugProvider to false or run in the Development environment.");

    if (clientSettings.Recaptcha?.Enabled == true
        && string.IsNullOrWhiteSpace(clientSettings.Recaptcha.PrivateKey))
        throw new InvalidOperationException(
            "Recaptcha.Enabled is true but Recaptcha.PrivateKey is empty. " +
            "Provide a valid reCAPTCHA private key or disable reCAPTCHA.");

    // WARNING — log and continue
    if (emailNotifSettings.Enabled && string.IsNullOrWhiteSpace(smtpSettings.Host))
        Log.Warning(
            "EmailNotificationSettings.Enabled is true but SmtpSettings.Host is empty. " +
            "Password-changed notification emails will be silently discarded.");

    if (expirySettings.Enabled && string.IsNullOrWhiteSpace(smtpSettings.Host))
        Log.Warning(
            "PasswordExpiryNotificationSettings.Enabled is true but SmtpSettings.Host is empty. " +
            "Expiry notification emails will be silently discarded.");

    if (passwordChangeOptions.PortalLockoutThreshold >= 10)
        Log.Warning(
            "PortalLockoutThreshold is {Threshold} (>= 10). This is unusually high and may indicate a misconfiguration. Typical values are 3-5.",
            passwordChangeOptions.PortalLockoutThreshold);

    if (webSettings.UseDebugProvider)
    {
        builder.Services.AddSingleton<DebugPasswordChangeProvider>();
        builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
            new LockoutPasswordChangeProvider(
                sp.GetRequiredService<DebugPasswordChangeProvider>(),
                sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
                sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
        builder.Services.AddSingleton<IPasswordChangeProvider>(sp =>
            sp.GetRequiredService<LockoutPasswordChangeProvider>());
        builder.Services.AddSingleton<ILockoutDiagnostics>(sp =>
            sp.GetRequiredService<LockoutPasswordChangeProvider>());
        builder.Services.AddSingleton<IEmailService, NoOpEmailService>();
    }
    else
    {
        builder.Services.AddSingleton<PasswordChangeProvider>();
        builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
            new LockoutPasswordChangeProvider(
                sp.GetRequiredService<PasswordChangeProvider>(),
                sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
                sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
        builder.Services.AddSingleton<IPasswordChangeProvider>(sp =>
            sp.GetRequiredService<LockoutPasswordChangeProvider>());
        builder.Services.AddSingleton<ILockoutDiagnostics>(sp =>
            sp.GetRequiredService<LockoutPasswordChangeProvider>());
        builder.Services.AddTransient<IEmailService, SmtpEmailService>();

        if (expirySettings.Enabled)
            builder.Services.AddHostedService<PasswordExpiryNotificationService>();
    }

    // ─── SIEM service ─────────────────────────────────────────────────────────────
    builder.Services.AddSingleton<ISiemService, SiemService>();

    // ─── Rate limiting (built-in .NET 7+ API, no third-party dependency) ──────────
    // Policy name used by the [EnableRateLimiting] attribute on PasswordController.
    const string PasswordRateLimitPolicy = "password-fixed-window";

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.OnRejected = (context, _) =>
        {
            var siem = context.HttpContext.RequestServices.GetService<ISiemService>();
            var ip   = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            siem?.LogEvent(SiemEventType.RateLimitExceeded, "unknown", ip);
            return ValueTask.CompletedTask;
        };

        options.AddPolicy(PasswordRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit          = 5,
                    Window               = TimeSpan.FromMinutes(5),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit           = 0,  // no queuing — reject immediately
                }));
    });

    // ─── MVC / API ────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();

    // ─── Build app ────────────────────────────────────────────────────────────────
    var app = builder.Build();

    // ─── Request logging — one structured line per HTTP request ───────────────────
    app.UseSerilogRequestLogging();

    // ─── Security headers — applied to every response before any other middleware ─
    app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers["X-Frame-Options"]         = "DENY";
        headers["X-Content-Type-Options"]  = "nosniff";
        headers["Referrer-Policy"]         = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"]      = "geolocation=(), microphone=(), camera=()";
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' https://www.google.com/recaptcha/ https://www.gstatic.com/recaptcha/; " +
            "style-src 'self' 'unsafe-inline'; " +
            "frame-src https://www.google.com/recaptcha/ https://recaptcha.google.com/recaptcha/; " +
            "img-src 'self' data:; " +
            "connect-src 'self'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "object-src 'none'";

        if (webSettings.EnableHttpsRedirect)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        await next(context);
    });

    // ─── HTTPS redirect ───────────────────────────────────────────────────────────
    if (webSettings.EnableHttpsRedirect)
        app.UseHttpsRedirection();

    // ─── Static files and routing ─────────────────────────────────────────────────
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseRouting();

    // ─── Rate limiting — must come after UseRouting so endpoint metadata is resolved ─
    app.UseRateLimiter();

    app.MapControllers();

    // SPA fallback — serves index.html for non-API, non-file routes so deep links work.
    app.MapFallbackToFile("index.html");

    app.Run();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "PassReset terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
