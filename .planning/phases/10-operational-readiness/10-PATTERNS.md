# Phase 10: Operational Readiness — Pattern Map

**Mapped:** 2026-04-20
**Files analyzed:** 11 new/modified files
**Analogs found:** 10 / 11 (1 net-new: `security-allowlist.json`)

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/PassReset.Web/Services/IExpiryServiceDiagnostics.cs` (new) | service | request-response | `LockoutPasswordChangeProvider.cs` (ILockoutDiagnostics interface, lines 12–16) | exact |
| `src/PassReset.Web/Services/PasswordExpiryNotificationService.cs` (modified) | service | event-driven | self — add `IExpiryServiceDiagnostics` impl + `_lastTickTicks` field | exact |
| `src/PassReset.Web/Controllers/HealthController.cs` (modified) | controller | request-response | self — additive only; pattern: existing `CheckAdConnectivity()` + `ILockoutDiagnostics` injection | exact |
| `src/PassReset.Web/Program.cs` (modified) | config | request-response | self lines 141–171 — ILockoutDiagnostics cast registration pattern | exact |
| `deploy/Install-PassReset.ps1` (modified) | utility | request-response | self lines 918–938 — `$hostHeader`/`$selectedHttpPort`/`$HttpsPort` already computed | exact |
| `.github/workflows/tests.yml` (modified) | config | batch | self — existing `tests` job shape (checkout → setup-dotnet → setup-node → steps) | exact |
| `deploy/security-allowlist.json` (new) | config | batch | none — net-new JSON schema file | none |
| `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.tsx` (modified) | component | request-response | self — no disclosure widget today; `role="region"` already present | exact |
| `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` (modified) | component | request-response | self line 335 — move panel block above Username `<TextField>` | exact |
| `src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs` (new) | test | request-response | `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs` — WebApplicationFactory integration test pattern | role-match |
| `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.test.tsx` (new) | test | request-response | `src/PassReset.Web/ClientApp/src/components/PasswordStrengthMeter.test.tsx` — RTL component render test | exact |

---

## Pattern Assignments

### `src/PassReset.Web/Services/IExpiryServiceDiagnostics.cs` (service interface, new)

**Analog:** `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs` lines 12–16

**Interface definition pattern:**
```csharp
// Analog: ILockoutDiagnostics (LockoutPasswordChangeProvider.cs:12-16)
public interface ILockoutDiagnostics
{
    /// <summary>Number of active (non-expired) lockout entries.</summary>
    int ActiveEntries { get; }
}
```

**Copy this shape exactly:**
```csharp
// New file: IExpiryServiceDiagnostics.cs
namespace PassReset.Web.Services;

/// <summary>
/// Exposes expiry notification service diagnostics for health monitoring.
/// </summary>
public interface IExpiryServiceDiagnostics
{
    /// <summary>Whether the service is configured and running.</summary>
    bool IsEnabled { get; }
    /// <summary>UTC timestamp of the last successful notification tick. Null if never run.</summary>
    DateTimeOffset? LastTickUtc { get; }
}
```

**Namespace:** `PassReset.Web.Services` (matches `PasswordExpiryNotificationService.cs` line 5)

---

### `src/PassReset.Web/Services/PasswordExpiryNotificationService.cs` (modified — add diagnostics)

**Analog:** self + `LockoutPasswordChangeProvider.cs` Interlocked pattern

**Class declaration change** (line 11 — add interface):
```csharp
// Before:
internal sealed class PasswordExpiryNotificationService : BackgroundService

// After:
internal sealed class PasswordExpiryNotificationService : BackgroundService, IExpiryServiceDiagnostics
```

**New field** (add after existing fields, ~line 23):
```csharp
// Atomic tick storage — DateTimeOffset encoded as long UTC ticks (0 = never run).
private long _lastTickTicks;
```

**Interface implementation** (add as public properties):
```csharp
// IExpiryServiceDiagnostics — atomic reads, no lock needed
public bool IsEnabled => _notifSettings.Enabled;
public DateTimeOffset? LastTickUtc =>
    _lastTickTicks == 0 ? null : new DateTimeOffset(Interlocked.Read(ref _lastTickTicks), TimeSpan.Zero);
```

**Tick update** (add at end of each `RunNotificationsAsync` call, inside `ExecuteAsync` loop):
```csharp
Interlocked.Exchange(ref _lastTickTicks, DateTimeOffset.UtcNow.UtcTicks);
```

---

### `src/PassReset.Web/Controllers/HealthController.cs` (modified — extend)

**Analog:** self — existing constructor injection + `CheckAdConnectivity()` pattern (lines 14–27, 51–92)

**Imports to add:**
```csharp
// Add to existing using block:
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using PassReset.Web.Models;   // SmtpSettings, EmailNotificationSettings, PasswordExpiryNotificationSettings
using PassReset.Web.Services; // IExpiryServiceDiagnostics
```

**Constructor extension** (add 3 new injected fields to existing pattern):
```csharp
// Existing pattern (lines 19-27) — extend, do NOT replace:
private readonly IOptions<SmtpSettings> _smtp;
private readonly IOptions<EmailNotificationSettings> _emailNotif;
private readonly IOptions<PasswordExpiryNotificationSettings> _expiryNotif;
private readonly IExpiryServiceDiagnostics _expiryDiagnostics;
```

**Method signature change** — `Get()` must become async (R-01 validated):
```csharp
// Before (line 33):
public IActionResult Get()

// After:
public async Task<IActionResult> GetAsync()
```

**New response shape** (replace existing anonymous object at lines 37–46):
```csharp
var adResult   = await CheckAdConnectivityAsync();   // refactored from sync
var smtpResult = await CheckSmtpAsync();
var expResult  = CheckExpiryService();

var aggregate = new[] { adResult.status, smtpResult.status, expResult.status }
    .Contains("unhealthy") ? "unhealthy"
    : new[] { adResult.status, smtpResult.status, expResult.status }.Contains("degraded") ? "degraded"
    : "healthy";

var result = new
{
    status    = aggregate,
    timestamp = DateTimeOffset.UtcNow,
    checks    = new
    {
        ad            = new { status = adResult.status,  latency_ms = adResult.latencyMs,  last_checked = DateTimeOffset.UtcNow },
        smtp          = new { status = smtpResult.status, latency_ms = smtpResult.latencyMs, last_checked = DateTimeOffset.UtcNow, skipped = smtpResult.skipped },
        expiryService = new { status = expResult.status,  latency_ms = expResult.latencyMs,  last_checked = DateTimeOffset.UtcNow },
    },
};

return aggregate == "healthy" ? Ok(result) : StatusCode(503, result);
```

**SMTP probe private method** (new, inline — D-02):
```csharp
private async Task<(string status, long latencyMs, bool skipped)> CheckSmtpAsync()
{
    var emailEnabled  = _emailNotif.Value.Enabled;
    var expiryEnabled = _expiryNotif.Value.Enabled;

    if (!emailEnabled && !expiryEnabled)
        return ("skipped", 0, true);

    var sw = Stopwatch.StartNew();
    try
    {
        using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        await client.ConnectAsync(_smtp.Value.Host, _smtp.Value.Port, cts.Token);
        return ("healthy", sw.ElapsedMilliseconds, false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "SMTP health check failed ({Host}:{Port})", _smtp.Value.Host, _smtp.Value.Port);
        return ("unhealthy", sw.ElapsedMilliseconds, false);
    }
}
```

**ExpiryService probe private method** (new, sync):
```csharp
private (string status, long latencyMs) CheckExpiryService()
{
    if (!_expiryDiagnostics.IsEnabled)
        return ("not-enabled", 0);

    // Healthy if: ran at least once AND last tick is within configured interval + 30min slack
    if (_expiryDiagnostics.LastTickUtc is null)
        return ("degraded", 0);  // enabled but never ran

    return ("healthy", 0);
}
```

**AD probe** — refactor existing `CheckAdConnectivity()` to async, replacing sync `TcpClient.Connect()` (line 81) with `ConnectAsync(host, port, cts.Token)` using same 3s timeout pattern as SMTP probe.

---

### `src/PassReset.Web/Program.cs` (modified — DI registration)

**Analog:** self lines 141–171 — ILockoutDiagnostics cast registration pattern

**Debug branch addition** (after line 152, inside `if (webSettings.UseDebugProvider)` block):
```csharp
// Null-object for debug mode — ExpiryService not registered; HealthController still resolves
builder.Services.AddSingleton<IExpiryServiceDiagnostics>(new NullExpiryServiceDiagnostics());
```

**Production branch addition** (after line 170, inside `else` block):
```csharp
// When expirySettings.Enabled=true: cast the hosted service (same pattern as ILockoutDiagnostics)
if (expirySettings.Enabled)
{
    builder.Services.AddSingleton<PasswordExpiryNotificationService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PasswordExpiryNotificationService>());
    builder.Services.AddSingleton<IExpiryServiceDiagnostics>(sp =>
        sp.GetRequiredService<PasswordExpiryNotificationService>());
}
else
{
    builder.Services.AddSingleton<IExpiryServiceDiagnostics>(new NullExpiryServiceDiagnostics());
}
```

**NullExpiryServiceDiagnostics** (inline private class or dedicated file):
```csharp
private sealed class NullExpiryServiceDiagnostics : IExpiryServiceDiagnostics
{
    public bool IsEnabled => false;
    public DateTimeOffset? LastTickUtc => null;
}
```

---

### `deploy/Install-PassReset.ps1` (modified — post-deploy verification block)

**Analog:** self lines 918–938 — existing URL announcement block

**URL variables already in scope** (lines 919, 921, 937):
```powershell
$hostHeader        # = $env:COMPUTERNAME (line 919)
$selectedHttpPort  # resolved HTTP port (line 921)
$HttpsPort         # resolved HTTPS port (line 937) — $null if no cert
$CertThumbprint    # $null if HTTP-only install
```

**New `-SkipHealthCheck` parameter** (add to `param()` block at top of script, same style as existing switches):
```powershell
[switch]$SkipHealthCheck = $false
```

**Post-deploy block** (insert immediately after the STAB-001 URL announcement block, ~line 938):
```powershell
# STAB-019: post-deploy verification
if (-not $SkipHealthCheck) {
    $baseUrl = if ($CertThumbprint) {
        "https://${hostHeader}:${HttpsPort}"
    } else {
        "http://${hostHeader}:${selectedHttpPort}"
    }

    $maxAttempts = 10; $attempt = 0; $ok = $false
    Write-Step "Verifying deployment at $baseUrl (up to $maxAttempts attempts)"

    do {
        Start-Sleep -Seconds 2
        $attempt++
        try {
            $h = Invoke-WebRequest -Uri "$baseUrl/api/health"   -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            $s = Invoke-WebRequest -Uri "$baseUrl/api/password" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($h.StatusCode -eq 200 -and $s.StatusCode -eq 200) {
                $body = $h.Content | ConvertFrom-Json
                $ok   = $true
            }
        } catch {
            Write-Warning "Attempt ${attempt}/${maxAttempts}: $_"
        }
    } while (-not $ok -and $attempt -lt $maxAttempts)

    if (-not $ok) {
        Write-Error "Post-deploy health check failed after ${maxAttempts} attempts. Last response: $($h.Content)"
        exit 1
    }

    $ad    = $body.checks.ad.status
    $smtp  = $body.checks.smtp.status
    $expir = $body.checks.expiryService.status
    Write-Ok "Health OK -- AD: $ad, SMTP: $smtp, ExpiryService: $expir"
}
```

---

### `.github/workflows/tests.yml` (modified — security-audit job)

**Analog:** self — existing `tests` job structure (lines 10–57)

**Job shape to copy:**
```yaml
# Existing tests job pattern:
tests:
  runs-on: windows-latest
  steps:
    - uses: actions/checkout@v6.0.2
    - name: Setup .NET
      uses: actions/setup-dotnet@v5.2.0
      with:
        dotnet-version: '10.0.x'
    - name: Setup Node.js
      uses: actions/setup-node@v6.3.0
      with:
        node-version: '22'
        cache: 'npm'
        cache-dependency-path: src/PassReset.Web/ClientApp/package-lock.json
```

**New job** (add at end of `jobs:` block — no `needs:` dependency, runs parallel with `tests`):
```yaml
security-audit:
  runs-on: windows-latest
  steps:
    - uses: actions/checkout@v6.0.2

    - name: Setup .NET
      uses: actions/setup-dotnet@v5.2.0
      with:
        dotnet-version: '10.0.x'

    - name: Setup Node.js
      uses: actions/setup-node@v6.3.0
      with:
        node-version: '22'
        cache: 'npm'
        cache-dependency-path: src/PassReset.Web/ClientApp/package-lock.json

    - name: Install npm dependencies
      working-directory: src/PassReset.Web/ClientApp
      run: npm ci

    - name: npm audit (high+critical gate)
      working-directory: src/PassReset.Web/ClientApp
      shell: pwsh
      run: |
        $audit = npm audit --json 2>&1 | Out-String
        $result = $audit | ConvertFrom-Json
        $allowlist = (Get-Content ../../../deploy/security-allowlist.json | ConvertFrom-Json).advisories
        $today = [datetime]::UtcNow.ToString("yyyy-MM-dd")
        $validIds = $allowlist | Where-Object { $_.expires -gt $today -and $_.scope -eq "npm" } | Select-Object -ExpandProperty id
        $unfixed = $result.vulnerabilities.PSObject.Properties.Value |
          Where-Object { $_.severity -in @("high","critical") } |
          Where-Object {
            $advisoryIds = @($_.via | Where-Object { $_ -is [psobject] } | ForEach-Object { ($_.url -replace ".*/","") })
            ($advisoryIds | Where-Object { $_ -notin $validIds }).Count -gt 0
          }
        if ($unfixed) {
          $unfixed | ForEach-Object { Write-Warning "Unfixed: $($_.name) [$($_.severity)]" }
          Write-Error "npm audit: unfixed high/critical advisories found"
          exit 1
        }
        Write-Host "npm audit: no unfixed high/critical advisories"

    - name: dotnet audit (high+critical gate)
      shell: pwsh
      run: |
        $output = dotnet list src/PassReset.sln package --vulnerable --include-transitive 2>&1
        $allowlist = (Get-Content deploy/security-allowlist.json | ConvertFrom-Json).advisories
        $today = [datetime]::UtcNow.ToString("yyyy-MM-dd")
        $validIds = $allowlist | Where-Object { $_.expires -gt $today -and $_.scope -eq "nuget" } | Select-Object -ExpandProperty id
        $highLines = $output | Select-String -Pattern "(High|Critical)" -CaseSensitive:$false
        $unfixed = $highLines | Where-Object {
          $id = [regex]::Match($_, "GHSA-[a-z0-9\-]+").Value
          $id -and $id -notin $validIds
        }
        if ($unfixed) {
          $unfixed | ForEach-Object { Write-Warning $_ }
          Write-Error "dotnet audit: unfixed high/critical advisories found"
          exit 1
        }
        Write-Host "dotnet audit: no unfixed high/critical advisories"

    - name: Post audit summary
      if: always()
      shell: pwsh
      run: |
        echo "## Security Audit Results" >> $env:GITHUB_STEP_SUMMARY
        echo "Scanned: npm (Node 22 / npm 10+) + dotnet NuGet packages" >> $env:GITHUB_STEP_SUMMARY
        echo "Gate: high + critical only; moderate/low printed as warnings" >> $env:GITHUB_STEP_SUMMARY
```

---

### `deploy/security-allowlist.json` (new — no analog)

**Schema** (D-12 decision, RESEARCH.md recommendation):
```json
{
  "_readme": "Add entries to suppress known advisories. Each entry expires in <=90 days — re-review required. scope: npm | nuget",
  "advisories": []
}
```

---

### `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.tsx` (modified — no code change needed)

**Analog:** self (entire file, lines 1–54)

The component has no disclosure widget today. `role="region"` + `aria-label="Password requirements"` already present (lines 35–36). No `<Collapse>` needed per RESEARCH.md R-04 validation. The only required change is in `PasswordForm.tsx` (panel position) and `appsettings.json` (default value).

---

### `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` (modified — panel position)

**Analog:** self — move existing `{settings.showAdPasswordPolicy && <AdPasswordPolicyPanel ... />}` block (currently line 335, after CurrentPassword) to above the Username `<TextField>`.

**Pattern to copy** — existing guard + component (no structural change, only location):
```tsx
// Move this block from after CurrentPassword to BEFORE the Username TextField:
{settings.showAdPasswordPolicy && (
  <AdPasswordPolicyPanel policy={policy} loading={policyLoading} />
)}
```

**appsettings.json change** — `ShowAdPasswordPolicy` default:
```json
// ClientSettings section: change false → true
"ShowAdPasswordPolicy": true
```

---

### `src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs` (new)

**Analog:** `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs` — WebApplicationFactory integration pattern

**Imports to copy** (PasswordControllerTests.cs lines 1–10):
```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
```

**Class skeleton** (copy WebApplicationFactory pattern):
```csharp
namespace PassReset.Tests.Web.Controllers;

public class HealthControllerTests : IDisposable
{
    private readonly DebugFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Get_ReturnsOk_WithNestedChecksShape()
    {
        var client   = NewClient();
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponseDto>();
        Assert.NotNull(body?.Checks?.Ad);
        Assert.NotNull(body?.Checks?.Smtp);
        Assert.NotNull(body?.Checks?.ExpiryService);
    }

    [Fact]
    public async Task Get_SmtpSkipped_WhenBothEmailFeaturesDisabled()
    {
        // DebugFactory has UseDebugProvider=true → email disabled by default
        var client   = NewClient();
        var response = await client.GetAsync("/api/health");
        var body     = await response.Content.ReadFromJsonAsync<HealthResponseDto>();
        Assert.True(body?.Checks?.Smtp?.Skipped);
    }

    // Wire-shaped DTOs (same pattern as PasswordControllerTests ApiResultDto):
    private sealed class HealthResponseDto { ... }
}
```

---

### `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.test.tsx` (new)

**Analog:** `src/PassReset.Web/ClientApp/src/components/PasswordStrengthMeter.test.tsx` (lines 1–38)

**Imports pattern** (copy exactly):
```tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import AdPasswordPolicyPanel from './AdPasswordPolicyPanel';
```

**Test pattern** (copy describe/it/expect shape):
```tsx
describe('AdPasswordPolicyPanel', () => {
  const mockPolicy = { minLength: 8, requiresComplexity: true, historyLength: 3 };

  it('renders nothing when policy is null', () => {
    const { container } = render(<AdPasswordPolicyPanel policy={null} loading={false} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders region landmark when policy is available', () => {
    render(<AdPasswordPolicyPanel policy={mockPolicy} loading={false} />);
    expect(screen.getByRole('region', { name: /password requirements/i })).toBeTruthy();
  });

  it('renders skeleton when loading', () => {
    // MUI Skeleton renders — container not null
    const { container } = render(<AdPasswordPolicyPanel policy={null} loading={true} />);
    expect(container.firstChild).not.toBeNull();
  });
});
```

---

## Shared Patterns

### Diagnostics Interface Registration (applies to IExpiryServiceDiagnostics in Program.cs)
**Source:** `src/PassReset.Web/Program.cs` lines 151–152 and 165–166
```csharp
// Pattern: register concrete type as singleton, then cast to diagnostic interface
builder.Services.AddSingleton<ILockoutDiagnostics>(sp =>
    sp.GetRequiredService<LockoutPasswordChangeProvider>());
```
Apply to: both debug and production branches for `IExpiryServiceDiagnostics`.

### Null-Object for unregistered diagnostics (applies to debug + expiry-disabled paths)
**Source:** implied by `ILockoutDiagnostics` always being registered — HealthController never gets null.
Pattern: register a `NullExpiryServiceDiagnostics` (IsEnabled=false, LastTickUtc=null) rather than using nullable injection.

### XML Doc Comments on public members
**Source:** `LockoutPasswordChangeProvider.cs` lines 7–16, `HealthController.cs` lines 7–13
Apply to: `IExpiryServiceDiagnostics` interface and all new public properties/methods.

### Logging pattern (structured, semantic)
**Source:** `HealthController.cs` lines 86–87, 90
```csharp
_logger.LogWarning(ex, "SMTP health check failed ({Host}:{Port})", smtp.Host, smtp.Port);
```
Apply to: all new probe methods in HealthController.

### xUnit test DTO pattern (wire-shaped deserialization)
**Source:** `PasswordControllerTests.cs` lines 41–59
```csharp
// Getter-only properties can't be populated by STJ — use private sealed DTOs with [JsonPropertyName]
private sealed class ApiResultDto { [JsonPropertyName("errors")] public List<ApiErrorItemDto> Errors { get; set; } = new(); }
```
Apply to: `HealthControllerTests.cs` nested `HealthResponseDto` + `CheckDto`.

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `deploy/security-allowlist.json` | config | batch | Net-new JSON schema file; no similar allowlist files exist in project. Use RESEARCH.md schema directly. |

---

## Metadata

**Analog search scope:** `src/PassReset.Web/`, `src/PassReset.Tests/`, `src/PassReset.PasswordProvider/`, `.github/workflows/`, `deploy/`
**Files scanned:** 13
**Pattern extraction date:** 2026-04-20
