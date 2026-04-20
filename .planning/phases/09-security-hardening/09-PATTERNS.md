# Phase 9: Security Hardening - Pattern Map

**Mapped:** 2026-04-17
**Files analyzed:** 14 (6 new code, 5 modified code, 1 modified installer, 1 modified test, 6 docs)
**Analogs found:** 11 / 11 non-doc files

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/PassReset.Tests/Web/Controllers/PasswordControllerRedactionTests.cs` (STAB-013) | integration-test | request-response | `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs` | exact |
| `src/PassReset.Tests/Web/Controllers/PasswordControllerRateLimitTests.cs` (STAB-014 a/b) | integration-test | request-response | `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs` | exact |
| `src/PassReset.Tests/Web/Controllers/PasswordControllerRecaptchaTests.cs` (STAB-014 c/d) | integration-test | request-response | `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs` | exact |
| `src/PassReset.Tests/Web/Startup/HstsHeaderTests.cs` (STAB-016) | integration-test | request-response | `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` | exact (WebApplicationFactory subclass pattern) |
| `src/PassReset.Tests/Web/Startup/EnvVarConfigurationTests.cs` (STAB-017) | integration-test | request-response / config | `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` | exact |
| `src/PassReset.Tests/Web/Services/AuditEventShapeTests.cs` (STAB-015) | unit-test (reflection) | n/a | `src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs` | role-match |
| `src/PassReset.Web/Services/AuditEvent.cs` (STAB-015) | model (record DTO) | n/a | `src/PassReset.Web/Models/PwnedCheckRequest.cs` / `SiemSettings.cs` | role-match (allowlist record) |
| `src/PassReset.Web/Controllers/PasswordController.cs` (MOD STAB-013) | controller | request-response | self (existing error-return block lines 191-198) | n/a — edit in place |
| `src/PassReset.Web/Services/SiemService.cs` (MOD STAB-015) | service | event-driven | self (existing `LogEvent` lines 59-66) | n/a — edit in place |
| `src/PassReset.Web/Services/SiemSyslogFormatter.cs` (MOD STAB-015) | utility (pure static) | transform | self (existing `Format` lines 14-31) | n/a — extend |
| `src/PassReset.Web/Services/ISiemService.cs` (MOD STAB-015) | interface | n/a | self — add `LogEvent(AuditEvent)` overload | n/a — edit |
| `src/PassReset.Web/Models/SiemSettings.cs` (MOD STAB-015 D-20) | settings | config | self (existing `SyslogSettings` lines 11-33) | n/a — add SdId |
| `src/PassReset.Web/Models/SiemSettingsValidator.cs` (MOD STAB-015 D-20) | validator | config | self (existing validator lines 17-89) | n/a — extend |
| `src/PassReset.Web/Program.cs` (NO CODE CHANGE — verify only) | bootstrap | config | self | n/a — HSTS + env-var paths already wired |
| `deploy/Install-PassReset.ps1` (MOD STAB-016) | installer | filesystem | self (existing `Get-WebBinding` block lines 877-916) | n/a — add post-binding helper |
| `src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs` (MOD STAB-015) | unit-test | transform | self | n/a — extend with SD-ELEMENT cases |

---

## Pattern Assignments

### New test classes (STAB-013, STAB-014) — `PasswordController*Tests.cs`

**Analog:** `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs`

**Why this analog:** Already proves `WebApplicationFactory<Program>` re-entry works; uses per-instance `DebugFactory` to isolate rate-limiter state; exposes `ApiResultDto`/`ApiErrorItemDto` wire-shape DTOs that must be reused (getter-only `ApiResult`/`ApiErrorItem` cannot be deserialized by System.Text.Json).

**Imports pattern** (lines 1-8):
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PassReset.Common;
using PassReset.Web.Models;
```

**Fixture pattern — per-test factory isolation** (lines 17-29, 158-180):
```csharp
public class PasswordControllerTests : IDisposable
{
    // Fresh factory per test instance — isolates the in-memory rate limiter state
    // so the 5-req/5-min fixed window policy does not leak across tests.
    private readonly DebugFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    public sealed class DebugFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["WebSettings:UseDebugProvider"]                 = "true",
                    ["WebSettings:EnableHttpsRedirect"]              = "false",
                    ["ClientSettings:MinimumDistance"]               = "0",
                    ["ClientSettings:Recaptcha:Enabled"]             = "false",
                    ["EmailNotificationSettings:Enabled"]            = "false",
                    ["PasswordExpiryNotificationSettings:Enabled"]   = "false",
                    ["SiemSettings:Syslog:Enabled"]                  = "false",
                    ["SiemSettings:AlertEmail:Enabled"]              = "false",
                    ["PasswordChangeOptions:PortalLockoutThreshold"] = "0",
                    ["PasswordChangeOptions:UseAutomaticContext"]    = "true",
                });
            });
        }
    }
}
```

**Wire-shape DTO pattern — do NOT deserialize `ApiResult`/`ApiErrorItem` directly** (lines 42-64):
```csharp
// ApiResult/ApiErrorItem expose data as getter-only properties — System.Text.Json
// cannot populate those on deserialization. Wire-shaped DTOs for tests.
private sealed class ApiResultDto
{
    [JsonPropertyName("errors")]
    public List<ApiErrorItemDto> Errors { get; set; } = new();

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}

private sealed class ApiErrorItemDto
{
    [JsonPropertyName("errorCode")]
    public ApiErrorCode ErrorCode { get; set; }

    [JsonPropertyName("fieldName")]
    public string? FieldName { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

private static async Task<ApiResultDto?> ReadResultAsync(HttpResponseMessage response) =>
    await response.Content.ReadFromJsonAsync<ApiResultDto>();
```

**Request-build helper pattern** (lines 31-38):
```csharp
private static ChangePasswordModel MakeRequest(string username) => new()
{
    Username          = username,
    CurrentPassword   = "OldPassword1!",
    NewPassword       = "BrandNewP@ssword123",
    NewPasswordVerify = "BrandNewP@ssword123",
    Recaptcha         = string.Empty,
};
```

**Assertion pattern** (lines 96-105):
```csharp
[Fact]
public async Task Post_UserNotFoundMagicUser_ReturnsUserNotFoundErrorCode()
{
    using var client = NewClient();
    var response = await client.PostAsJsonAsync("/api/password", MakeRequest("userNotFound"));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    var result = await ReadResultAsync(response);
    Assert.Contains(result!.Errors, e => e.ErrorCode == ApiErrorCode.UserNotFound);
}
```

**STAB-013 factory variant — must override environment to Production** (derived from Pitfall 2 in RESEARCH):
```csharp
public sealed class ProductionEnvFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");  // CRITICAL for STAB-013 gate
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Copy all DebugFactory keys so provider still runs in-process.
                ["WebSettings:UseDebugProvider"]    = "true",
                ["WebSettings:EnableHttpsRedirect"] = "false",
                // ... same as DebugFactory ...
            });
        });
    }
}
```

**Magic usernames available in DebugPasswordChangeProvider** (`src/PassReset.Web/Helpers/DebugPasswordChangeProvider.cs` lines 28-42): `"invalidCredentials"`, `"userNotFound"`, `"changeNotPermitted"`, `"invalidCaptcha"`, `"passwordTooYoung"`, `"fieldMismatch"`, `"fieldRequired"`, `"error"`, `"invalidDomain"`, `"ldapProblem"`, `"pwnedPassword"`. Any other username = success.

---

### New file `src/PassReset.Web/Services/AuditEvent.cs` (STAB-015)

**Analog for file placement + record style:** `src/PassReset.Web/Models/PwnedCheckRequest.cs` (simple record-style DTO) and co-location convention of `ISiemService.cs` (enum + interface in same file, Services folder).

**Place in `Services/` alongside `ISiemService.cs`** (matches RESEARCH §"Recommended Project Structure" line 158).

**Shape locked by D-10** (RESEARCH Pattern 3, lines 244-259):
```csharp
namespace PassReset.Web.Services;

/// <summary>
/// Allowlist DTO for audit events. No secret fields exist on this type by design —
/// compile-time redaction. Do NOT add Password, Token, PrivateKey, or any field
/// that would violate STAB-015's redaction guarantee.
/// </summary>
public sealed record AuditEvent(
    SiemEventType EventType,
    string Outcome,
    string Username,
    string? ClientIp = null,
    string? TraceId = null,
    string? Detail = null);
```

**Conventions enforced:**
- File-scoped namespace (project convention)
- `sealed record` (matches `SiemService` sealed pattern)
- XML doc on public type
- No `[JsonIgnore]` needed — this DTO is internal to SIEM emission, never serialized to client

---

### MOD `src/PassReset.Web/Services/ISiemService.cs` (STAB-015)

**Current surface** (lines 43-47 — the full interface):
```csharp
public interface ISiemService
{
    /// <summary>Records a security event synchronously (no async I/O on the hot path).</summary>
    void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null);
}
```

**Add overload — keep existing signature for back-compat** (per RESEARCH lines 418-423):
```csharp
public interface ISiemService
{
    void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null);
    /// <summary>STAB-015: structured audit event via allowlist DTO.</summary>
    void LogEvent(AuditEvent evt);
}
```

---

### MOD `src/PassReset.Web/Services/SiemService.cs` (STAB-015)

**Current `LogEvent` surface** (lines 59-66):
```csharp
public void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null)
{
    if (_settings.Syslog.Enabled)
        EmitSyslog(eventType, username, ipAddress, detail);

    if (_settings.AlertEmail.Enabled)
        EnqueueAlertEmail(eventType, username, ipAddress, detail);
}
```

**Current `EmitSyslog` surface showing the formatter call** (lines 70-101 excerpt — this is the exact call site that must grow an `AuditEvent`-aware variant):
```csharp
private void EmitSyslog(SiemEventType eventType, string username, string ipAddress, string? detail)
{
    try
    {
        var syslog   = _settings.Syslog;
        var severity = SeverityMap.GetValueOrDefault(eventType, 5);
        var hostname = Dns.GetHostName();

        // RFC 5424 formatting delegated to pure static helper (testable without sockets).
        var message = SiemSyslogFormatter.Format(
            timestampUtc: DateTimeOffset.UtcNow,
            facility:     syslog.Facility,
            severity:     severity,
            hostname:     hostname,
            appName:      syslog.AppName,
            eventType:    eventType.ToString(),
            username:     username,
            ipAddress:    ipAddress,
            detail:       detail);

        var bytes = Encoding.UTF8.GetBytes(message);
        // ... transport ...
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Syslog delivery failed for event {Event} user {User}", eventType, username);
    }
}
```

**Action pattern for STAB-015:**
1. Add `public void LogEvent(AuditEvent evt)` — delegates to a new `EmitSyslog(AuditEvent, SdId)` + `EnqueueAlertEmail(AuditEvent)`.
2. Pass new `SdId` from `_settings.Syslog.SdId` (D-20 config) into the formatter call.
3. Preserve **try/catch-swallow-and-log** invariant (lines 97-100) — SIEM must never throw on the hot path.
4. Legacy overload can internally construct `new AuditEvent(eventType, "legacy", username, ipAddress, null, detail)` and call the new path — single code path keeps testing surface small.

---

### MOD `src/PassReset.Web/Services/SiemSyslogFormatter.cs` (STAB-015)

**Current full surface** (all 56 lines — this is a pure static helper, no state):
```csharp
namespace PassReset.Web.Services;

public static class SiemSyslogFormatter
{
    public static string Format(
        DateTimeOffset timestampUtc,
        int facility,
        int severity,
        string hostname,
        string appName,
        string eventType,
        string username,
        string ipAddress,
        string? detail)
    {
        var priority = (facility * 8) + severity;
        var ts = timestampUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var detailPart = detail != null ? $" detail=\"{EscapeSd(detail)}\"" : string.Empty;

        return $"<{priority}>1 {ts} {hostname} {appName} - - - " +
               $"[PassReset@0 event=\"{eventType}\" user=\"{EscapeSd(username)}\" ip=\"{EscapeSd(ipAddress)}\"{detailPart}]";
    }

    public static string EscapeSd(string value)
    {
        var cleaned = StripControlChars(value);
        return cleaned.Replace("\\", "\\\\", StringComparison.Ordinal)
                      .Replace("\"", "\\\"", StringComparison.Ordinal)
                      .Replace("]",  "\\]",  StringComparison.Ordinal);
    }

    private static string StripControlChars(string input) =>
        string.Create(input.Length, input, static (span, src) =>
        {
            var pos = 0;
            foreach (var ch in src)
            {
                if (ch >= '\x20' && ch != '\x7F')
                    span[pos++] = ch;
            }
            span[pos..].Fill('\0');
        }).TrimEnd('\0');
}
```

**Key observation:** `EscapeSd` is already public and already handles `\`, `"`, `]`, and control chars (RESEARCH Pitfall 3 satisfied). Existing test `SiemSyslogFormatterTests.EscapeSd_EscapesBackslashQuoteAndClosingBracket` pins this.

**Action pattern for STAB-015:**
- Add new `Format(..., AuditEvent evt, string sdId)` overload OR extend the existing `Format` with a `structuredData` parameter.
- Replace hardcoded SD-ID `PassReset@0` with parameter (D-20 makes `passreset@32473` the new default; operator can override via `SiemSettings.Syslog.SdId`).
- Emit new SD-PARAMs: `outcome`, `traceId` (in addition to existing `event`, `user`, `ip`, `detail`).
- Reuse existing `EscapeSd` for every value.
- Keep old `Format` signature working OR update all callers — back-compat not strictly required since both callers are in same solution.

**Existing test file to extend (MOD):** `src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs` — already has `FixedTs`, `EscapeSd_*`, and `Format_UserWithInjectedClosingBracketIsEscaped` cases. Add:
- `Format_WithAuditEvent_EmitsOutcomeSdParam`
- `Format_WithAuditEvent_EmitsTraceIdSdParam`
- `Format_WithConfigurableSdId_UsesSetting`
- `Format_WithAuditEvent_EscapesOutcomeTraceIdAndDetail`

---

### MOD `src/PassReset.Web/Models/SiemSettings.cs` (STAB-015 D-20)

**Current `SyslogSettings`** (lines 11-33):
```csharp
public class SyslogSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 514;
    public string Protocol { get; set; } = "UDP";
    public int Facility { get; set; } = 10;
    public string AppName { get; set; } = "PassReset";
}
```

**Add** (D-20 — configurable SD-ID):
```csharp
/// <summary>
/// RFC 5424 SD-ID for the structured-data element emitted by SiemSyslogFormatter.
/// Default uses IANA reserved PEN 32473 (documentation/example); operators who have
/// registered their own Private Enterprise Number can override.
/// </summary>
public string SdId { get; set; } = "passreset@32473";
```

---

### MOD `src/PassReset.Web/Models/SiemSettingsValidator.cs` (STAB-015 D-20)

**Current validator surface** (lines 10-91 — pattern to extend):
```csharp
public sealed class SiemSettingsValidator : IValidateOptions<SiemSettings>
{
    private static readonly string[] ValidProtocols = ["UDP", "TCP"];

    private static string Fmt(string path, string reason, string actual)
        => $"{path}: {reason} (got \"{actual}\"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.";

    public ValidateOptionsResult Validate(string? name, SiemSettings options)
    {
        var failures = new List<string>();

        var syslog = options.Syslog;
        if (syslog is not null && syslog.Enabled)
        {
            if (string.IsNullOrWhiteSpace(syslog.Host))
                failures.Add(Fmt("SiemSettings.Syslog.Host", "must be non-empty when Syslog.Enabled is true", ""));

            if (syslog.Port <= 0 || syslog.Port > 65535)
                failures.Add(Fmt("SiemSettings.Syslog.Port", "must be a valid TCP/UDP port (1-65535)", ...));

            if (!ValidProtocols.Contains(syslog.Protocol, StringComparer.OrdinalIgnoreCase))
                failures.Add(Fmt("SiemSettings.Syslog.Protocol", "must be 'UDP' or 'TCP'", ...));
        }
        // ... alert email block ...

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
```

**Add rule for `SdId`** — RFC 5424 §6.3.2 allows `printusascii` minus `=`, space, `]`, `"`; length 1-32. For PEN form `name@PEN` this simplifies to: non-empty, no `=`, no space, no `]`, no `"`, ≤32 chars. Follow the `Fmt()` helper convention.

```csharp
if (string.IsNullOrEmpty(syslog.SdId)
    || syslog.SdId.Length > 32
    || syslog.SdId.IndexOfAny(new[] { ' ', '=', ']', '"' }) >= 0)
    failures.Add(Fmt(
        "SiemSettings.Syslog.SdId",
        "must be 1-32 RFC 5424 printusascii chars excluding '=', space, ']', '\"' (e.g. 'passreset@32473')",
        syslog.SdId ?? ""));
```

No Program.cs change needed — the validator is already registered at Program.cs lines 68-71.

---

### MOD `src/PassReset.Web/Controllers/PasswordController.cs` (STAB-013)

**Current error-return edge** (lines 189-198 — THE collapse site):
```csharp
var error = await _provider.PerformPasswordChangeAsync(model.Username, model.CurrentPassword, model.NewPassword);

if (error is not null)
{
    var siemType = MapErrorCodeToSiemEvent(error.ErrorCode);
    Audit($"Failed:{error.ErrorCode}", model.Username, clientIp, siemType, error.Message);
    var result = new ApiResult();
    result.Errors.Add(error);
    return BadRequest(result);
}
```

**Current constructor (lines 46-66)** — add `IHostEnvironment` as shown:
```csharp
public PasswordController(
    IPasswordChangeProvider provider,
    IEmailService emailService,
    ISiemService siemService,
    IOptions<ClientSettings> clientSettings,
    IOptions<EmailNotificationSettings> emailNotifSettings,
    PasswordPolicyCache policyCache,
    IPwnedPasswordChecker pwnedChecker,
    IOptions<PasswordChangeOptions> passwordOptions,
    ILogger<PasswordController> logger)
{
    _provider           = provider;
    // ... assign all other fields ...
    _logger             = logger;
}
```

**Action pattern (inline at collapse site — D-05 preserves Audit call order):**
```csharp
// existing field
private readonly IHostEnvironment _hostEnvironment;

// RESEARCH §"Code Examples" lines 407-413 — helper methods to add
private static bool IsAccountEnumerationCode(ApiErrorCode c) =>
    c is ApiErrorCode.InvalidCredentials or ApiErrorCode.UserNotFound;

private ApiErrorItem RedactIfProduction(ApiErrorItem err) =>
    _hostEnvironment.IsProduction() && IsAccountEnumerationCode(err.ErrorCode)
        ? new ApiErrorItem(ApiErrorCode.Generic, err.Message)  // D-04: reuse existing message
        : err;

// In PostAsync error branch:
if (error is not null)
{
    var siemType = MapErrorCodeToSiemEvent(error.ErrorCode);
    Audit($"Failed:{error.ErrorCode}", model.Username, clientIp, siemType, error.Message);

    // D-05: SIEM emission (above) stays granular; only the wire response collapses.
    var result = new ApiResult();
    result.Errors.Add(RedactIfProduction(error));
    return BadRequest(result);
}
```

**Do NOT modify `MapErrorCodeToSiemEvent` (lines 241-249)** — D-05 locks granularity.

**Second collapse site to consider — `ApiResult.FromModelStateErrors` branch at line 151:** validation failures are NOT account-enumeration codes (D-01 enumeration list excludes `ValidationFailed`), so no collapse needed there.

---

### MOD `deploy/Install-PassReset.ps1` (STAB-016)

**Analog — Phase 7 helper style (existing, lines 105-128):**
```powershell
function Write-Step  { param([string]$Msg) Write-Host "`n[>>] $Msg" -ForegroundColor Cyan }
function Write-Ok    { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green }
function Write-Warn  { param([string]$Msg) Write-Host "  [!!] $Msg" -ForegroundColor Yellow }

function Restore-StoppedForeignSites { ... }

function Abort       { param([string]$Msg) Restore-StoppedForeignSites; Write-Host "`n[ERR] $Msg`n" -ForegroundColor Red; exit 1 }
```

**Analog — existing `Get-WebBinding` usage for HTTPS cert bind (lines 877-916):**
```powershell
# HTTPS binding
if ($CertThumbprint) {
    $cert = Get-ChildItem Cert:\LocalMachine\My |
            Where-Object { $_.Thumbprint -eq $CertThumbprint } |
            Select-Object -First 1

    if (-not $cert) {
        Write-Warn "Certificate with thumbprint $CertThumbprint not found in LocalMachine\My — skipping HTTPS binding."
    } else {
        $existingBinding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -ErrorAction SilentlyContinue
        if ($existingBinding) { $existingBinding | Remove-WebBinding }

        New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -SslFlags 0
        $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort
        $binding.AddSslCertificate($CertThumbprint, 'My')
        Write-Ok "HTTPS binding configured on port $HttpsPort"
    }
} else {
    Write-Warn 'No certificate thumbprint supplied — HTTPS binding not configured. Add it manually or re-run with -CertThumbprint.'
}
```

**Analog — nested function + `[CmdletBinding()]` style (lines 138-139, 185-186, 206-207):**
```powershell
function Get-SchemaKeyManifest {
    [CmdletBinding()]
    param( ... )
    ...
}
```

**Action pattern for STAB-016 — add `Test-HttpsBinding` function and call it AFTER the cert-bind block (around line 917, BEFORE the "reachable URLs" announcement at line 918):**

```powershell
function Test-HttpsBinding {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SiteName)

    $httpsBindings = @(Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue)
    if ($httpsBindings.Count -eq 0) {
        Write-Warn "Site '$SiteName' has no HTTPS binding. PassReset requires TLS in production."
        Write-Warn "  - Re-run Install-PassReset.ps1 with -CertThumbprint <thumbprint> to configure HTTPS, or"
        Write-Warn "  - Add an HTTPS binding manually via IIS Manager before exposing the site."
        Write-Warn '  - For TLS-terminating reverse proxy setups, set WebSettings.EnableHttpsRedirect=false.'
    } else {
        Write-Ok "HTTPS binding verified on site '$SiteName'"
    }
}
```

**Placement (per RESEARCH Pitfall 6):** Place the function definition alongside other helpers (near line 128, after `Abort`), but invoke it AFTER `New-WebBinding` calls (after line 916). Do NOT invoke during fresh-install before `New-Website`.

**D-13 constraint:** warn-not-block. Even with `-Force`, emit the warning — do NOT call `Abort`.

---

### New `src/PassReset.Tests/Web/Startup/HstsHeaderTests.cs` and `EnvVarConfigurationTests.cs`

**Analog:** `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs`

**Why this analog:** Already co-located in the `Web/Startup/` folder. Establishes the subclass-per-factory pattern with detailed XML-doc on WHY subclasses (HostFactoryResolver intercept race). Shows `builder.UseEnvironment("Development")` override and `AddInMemoryCollection` config injection.

**Factory subclass pattern** (lines 28-52):
```csharp
public sealed class InvalidPasswordChangeOptionsFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSettings:UseDebugProvider"] = "true",
                ["WebSettings:EnableHttpsRedirect"] = "false",
                ["ClientSettings:Recaptcha:Enabled"] = "false",
                ["SmtpSettings:Host"] = "",
                ["SiemSettings:Syslog:Enabled"] = "false",
                // ... more config ...
            });
        });
    }
}
```

**Key adaptation for STAB-016:** Set `["WebSettings:EnableHttpsRedirect"] = "true"` and `builder.UseEnvironment("Production")` to exercise the HSTS branch in Program.cs line 256-257:
```csharp
if (webSettings.EnableHttpsRedirect)
    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
```
Then assert on `response.Headers.Contains("Strict-Transport-Security")` and value `max-age=31536000; includeSubDomains`.

**Key adaptation for STAB-017 (EnvVarConfigurationTests):** Use `Environment.SetEnvironmentVariable("SmtpSettings__Password", "...", EnvironmentVariableTarget.Process)` BEFORE constructing factory; factory reads `IOptions<SmtpSettings>.Value` via a test endpoint OR via `factory.Services.GetRequiredService<IOptions<SmtpSettings>>()`. Assert `.Value.Password == expected`. Clean up env var in `Dispose`/`finally`.

**Regression test for D-17 / Pitfall 5:** GET `/api/password` response JSON must NOT contain `"PrivateKey"` string even when `ClientSettings__Recaptcha__PrivateKey` env var is set — proves `[JsonIgnore]` (`ClientSettings.cs` line 57) still hides it from wire.

---

### New `src/PassReset.Tests/Web/Services/AuditEventShapeTests.cs` (STAB-015)

**Analog:** `src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs` (pure unit tests on service-layer types — no WebApplicationFactory).

**Style pattern:**
```csharp
using PassReset.Web.Services;

namespace PassReset.Tests.Web.Services;

public class SiemSyslogFormatterTests
{
    private static readonly DateTimeOffset FixedTs = new(...);

    [Fact]
    public void Format_ProducesRfc5424HeaderWithComputedPriority() { ... }
}
```

**Action pattern for AuditEventShapeTests — reflection-based allowlist assertion** (per RESEARCH Wave 0 gap):
```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using PassReset.Web.Services;

namespace PassReset.Tests.Web.Services;

public class AuditEventShapeTests
{
    [Fact]
    public void AuditEvent_HasNoSecretLookingProperties()
    {
        var forbidden = new Regex(@"password|token|secret|privatekey|apikey",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var props = typeof(AuditEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var leaks = props.Where(p => forbidden.IsMatch(p.Name)).Select(p => p.Name).ToList();

        Assert.Empty(leaks);
    }

    [Fact]
    public void AuditEvent_AllowlistedFieldsOnly()
    {
        var expected = new[] { "EventType", "Outcome", "Username", "ClientIp", "TraceId", "Detail" };
        var actual = typeof(AuditEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(expected.OrderBy(n => n).ToArray(), actual);
    }
}
```

---

## Shared Patterns

### Test — WebApplicationFactory re-entry guard (CRITICAL)
**Source:** `src/PassReset.Web/Program.cs` lines 30-37, 297-316; docs in `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` lines 14-21.
**Apply to:** ALL Phase 9 integration tests.
**Rule:** NEVER add a broad `catch (Exception)` or `finally { Log.CloseAndFlush(); }` to `Program.cs`. `WebApplicationFactory<Program>` signals handoff via an internal `StopTheHostException` that MUST propagate. Use per-instance `WebApplicationFactory<Program>` subclass, not inline `new WebApplicationFactory<Program>()`.

### Test — rate-limiter partition isolation (CRITICAL for STAB-014a)
**Source:** `PasswordControllerTests.cs` lines 19-23.
**Apply to:** `PasswordControllerRateLimitTests`, `PasswordControllerRecaptchaTests`, any test that calls POST more than once per test method.
**Rule:** Implement `IDisposable`; `new DebugFactory()` (or scenario-specific subclass) per test instance; dispose in `Dispose()`. This creates a fresh in-memory rate limiter state.

### Options-validator registration (for D-20 SdId rule)
**Source:** `src/PassReset.Web/Program.cs` lines 68-71. No change required — the existing `SiemSettingsValidator` registration automatically picks up new rules added inside the validator class.

### SIEM never-throws invariant
**Source:** `SiemService.cs` lines 70-101 (try/catch-swallow-and-log).
**Apply to:** New STAB-015 `LogEvent(AuditEvent)` overload and `EmitSyslogStructured` / `EnqueueAlertEmailFromEvent` helpers.
**Rule:** Wrap any transport/formatter call in try/catch; log via `_logger.LogError(ex, ...)`; never rethrow.

### RFC 5424 value escaping — reuse existing helper
**Source:** `SiemSyslogFormatter.EscapeSd` (public, lines 37-43).
**Apply to:** All new SD-PARAM value emission in STAB-015 formatter overload. Do NOT write a new escaper.

### Installer helper reuse (STAB-016)
**Source:** `Install-PassReset.ps1` lines 105-107 (`Write-Step`/`Write-Ok`/`Write-Warn`).
**Apply to:** `Test-HttpsBinding` function.
**Rule:** Never `Write-Host` directly for status messages; always go through the helpers. Do not invent `Write-Info` or similar.

### Config-key naming for env-var override (STAB-017)
**Source:** ASP.NET Core default (already wired via `WebApplication.CreateBuilder` — no code in `Program.cs` to add).
**Apply to:** All documentation updates.
**Rule:** Use `__` (double underscore) as path separator in env var names. Examples:
- `SmtpSettings__Password`
- `PasswordChangeOptions__ServiceAccountPassword`
- `ClientSettings__Recaptcha__PrivateKey`

Do NOT document or introduce a custom `PASSRESET_` prefix (D-16 locks default convention).

### Wire-shape DTO pattern for integration tests
**Source:** `PasswordControllerTests.cs` lines 42-64 (`ApiResultDto`, `ApiErrorItemDto`).
**Apply to:** Any integration test that needs to deserialize POST /api/password responses.
**Rule:** Never `ReadFromJsonAsync<ApiResult>()` — getter-only props fail deserialization. Use the existing `ApiResultDto` from `PasswordControllerTests.cs` (extract to a shared test utility if multiple test classes need it, or redeclare as `private sealed class` per the existing convention).

---

## No Analog Found

**None.** Every Phase 9 code surface maps to an existing analog in the codebase:
- Integration tests → `PasswordControllerTests.cs` + `StartupValidationTests.cs`
- Unit tests → `SiemSyslogFormatterTests.cs`
- Service DTO → `ISiemService.cs` co-location; record style mirrors `PwnedCheckRequest.cs`
- Controller edit → self (existing error-return edge)
- SIEM service edit → self (existing `LogEvent` + `EmitSyslog`)
- Formatter edit → self (pure static helper already tested)
- Settings + validator edit → self (existing SyslogSettings + SiemSettingsValidator)
- Installer → self (existing `Get-WebBinding` HTTPS cert-bind block + Phase 7 helpers)

**Docs (no pattern mapping — flag as docs-only):**
- `docs/IIS-Setup.md` (STAB-016/017)
- `docs/Secret-Management.md` (STAB-017)
- `docs/appsettings-Production.md` (STAB-017)
- `docs/Known-Limitations.md` (STAB-013)
- `CONTRIBUTING.md` (STAB-017 dev workflow)
- `CHANGELOG.md` ([Unreleased] entries for STAB-013..017)

## Metadata

**Analog search scope:**
- `src/PassReset.Tests/**` (all test files)
- `src/PassReset.Web/**` (controllers, services, models, Program.cs, middleware, helpers)
- `deploy/*.ps1` (installer scripts)

**Files scanned:** 41 C# production files, 28 test files, 3 PowerShell scripts, 2 JSON schemas.

**Pattern extraction date:** 2026-04-17
