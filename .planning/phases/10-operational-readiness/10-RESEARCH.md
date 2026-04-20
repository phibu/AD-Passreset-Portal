# Phase 10: Operational Readiness — Research

**Researched:** 2026-04-20
**Domain:** Health endpoint enrichment · Installer self-verification · CI security scans · Frontend UX polish
**Confidence:** HIGH — all findings verified directly from source files in this repo

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- D-01: Extend existing `/api/health` with nested `checks` object — additive, no new endpoint
- D-02: SMTP = TCP connect + 3 s timeout; skipped when both email features disabled
- D-03: ExpiryService healthy when running within interval+slack OR Enabled=false; use `IExpiryServiceDiagnostics` interface
- D-04: No secrets in response body; error detail to server log only
- D-05: All checks synchronous on request thread, no caching
- D-06: Post-deploy: hit `/api/health` + `GET /api/password` after AppPool recycle
- D-07: 10 retries × 2 s; hard-fail (exit ≥1) if still bad; print final response
- D-08: Success path prints "✓ Health OK — AD: healthy, SMTP: skipped, ExpiryService: not-enabled"
- D-09: Use installer's already-resolved scheme+host+port URL (STAB-001 output)
- D-10: `-SkipHealthCheck` switch; default `$false`
- D-11: CI fails on high+critical only; moderate=warning
- D-12: `deploy/security-allowlist.json` exception file with rationale + expiration ≤90 days
- D-13: Scans inside existing `tests.yml` reusable workflow as `security-audit` job
- D-14: Single `security-audit` job, `continue-on-error: false`, GitHub Action summary
- D-15: CodeQL/Dependabot remain independent
- D-16: `AdPasswordPolicyPanel` panel visible by default, no disclosure widget
- D-17: No FGPP lookup — deferred v2.0
- D-18: Panel pulls from `/api/password/policy` via `usePolicy.ts` — no server changes
- D-19: MUI `<Collapse defaultExpanded>` or equivalent; keyboard-navigable
- D-20: Four plans, sequential inline execution (no parallel — phase 9 parallel fallout)
- D-21: Tag v1.4.0 after phase 10 passes; then `/gsd-new-milestone` for v2.0

### Claude's Discretion
None declared — all gray areas accepted defaults (G1..G7).

### Deferred Ideas (OUT OF SCOPE)
- FGPP / PSO lookup (v2.0)
- Pester test toolchain for installer (deferred — no existing pattern)
- Any v2.0 platform work
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| STAB-018 | `/api/health` reports AD + SMTP + ExpiryService readiness without leaking secrets | HealthController.cs extended; two new interfaces modeled on `ILockoutDiagnostics` |
| STAB-019 | Installer post-deploy calls `/api/health` + `GET /api/password`; fails install on bad response | `$hostHeader` + `$selectedHttpPort`/`$HttpsPort` already computed at install end (line 921/937); URL reuse is direct |
| STAB-020 | CI security scans (`npm audit`, `dotnet list package --vulnerable`) gate on high+critical | `tests.yml` reusable workflow; add `security-audit` job; no `--format json` flag on dotnet — text parsing required |
| STAB-021 | Effective AD password policy visible by default above password fields | `showAdPasswordPolicy` defaults `false`; panel already exists; only wiring + layout tweak needed |
</phase_requirements>

---

## Summary

- **STAB-018** is the most complex requirement. `HealthController.cs` already has an `ILockoutDiagnostics` injection pattern (line 16, Program.cs line 151/165). Two new interfaces — `IExpiryServiceDiagnostics` and `ISmtpDiagnostics` — follow the same singleton-registered-via-cast pattern. The current health response shape already has a `checks` object but uses flat strings (`"ok"/"unreachable"`) — must be upgraded to the nested object shape from D-01 without breaking existing `status` field.
- **STAB-019** is straightforward. The installer already computes and prints `$hostHeader` + port at lines 921/937 (STAB-001 output). The post-deploy block reuses those variables; no new URL-discovery logic needed. No Pester tests exist — verification is manual UAT only.
- **STAB-020** requires careful parsing. `dotnet list package --vulnerable` has no `--format json` flag (verified). Text output must be parsed with PowerShell/bash regex in CI. `npm audit --json` is stable with npm 10+ (schema v2); CI uses Node 22 (npm 10+). Both scans fit inside `tests.yml` as an additive job.
- **STAB-021** is the smallest change. `AdPasswordPolicyPanel` is already a complete component. `showAdPasswordPolicy` defaults to `false` — D-16 requires making it `true` by default in `appsettings.json`, or changing the panel to render unconditionally when `policy` is available. The panel currently renders **after** the current-password field and before the new-password field (PasswordForm.tsx line 335). D-16 requires it above the fields — meaning above the username field or at minimum above the new-password field. Reading the form layout: Username → CurrentPassword → PolicyPanel → NewPassword. Moving it above username is the natural "above the fields" position per D-16.

**Primary recommendation:** Implement in plan order 10-01 → 10-02 → 10-03 → 10-04 sequentially. No plan has file overlap with the next until 10-04 (frontend only).

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| AD health probe | API / Backend | — | Existing `CheckAdConnectivity()` logic; synchronous TCP/PrincipalContext |
| SMTP health probe | API / Backend | — | New TCP connect; reads `SmtpSettings` from DI |
| ExpiryService health probe | API / Backend | — | BackgroundService exposes state via `IExpiryServiceDiagnostics` interface |
| Installer post-deploy check | Deploy / PowerShell | — | New block in `Install-PassReset.ps1`; uses already-resolved URL vars |
| CI security scan | CI / GitHub Actions | — | New `security-audit` job in `tests.yml`; parses tool output |
| Policy panel default-visible | Browser / Client | Frontend (React) | `showAdPasswordPolicy` default + `PasswordForm.tsx` layout change |

---

## Per-Requirement Research

### STAB-018 — `/api/health` enrichment

#### Existing state (verified from source)

**`HealthController.cs`** (entire file read):
- Currently injects `ILockoutDiagnostics` — confirms the diagnostics-interface pattern works in this controller
- Returns `checks: { ad: "ok"/"unreachable", lockout: { activeEntries: N } }` — flat string for AD, not a structured object
- `CheckAdConnectivity()` uses sync `TcpClient.Connect()` (no timeout) for the explicit-LDAP path (line 81)
- HTTP 200 on healthy, 503 on degraded

**`Program.cs`** (DI wiring verified at lines 151–165):
```csharp
// Both debug and production paths:
builder.Services.AddSingleton<ILockoutDiagnostics>(sp =>
    sp.GetRequiredService<LockoutPasswordChangeProvider>());
```
`LockoutPasswordChangeProvider` implements both `IPasswordChangeProvider` and `ILockoutDiagnostics` — registered once as singleton, cast to the diagnostic interface. This is the exact pattern to replicate for `IExpiryServiceDiagnostics`.

**`PasswordExpiryNotificationService.cs`** (entire file read):
- No `LastTick` field exists today — must be added
- The service is `internal sealed` and registered conditionally (line 170: only when `expirySettings.Enabled`)
- No `IHostedServiceDiagnostics` or equivalent exists — `IExpiryServiceDiagnostics` is net-new
- The service is NOT registered when `UseDebugProvider=true` — `IExpiryServiceDiagnostics` must handle the "not registered" case gracefully (null-object pattern or conditional registration)

#### What needs to be built

1. **`IExpiryServiceDiagnostics`** interface (new file in `PassReset.Web/Services/`):
   ```csharp
   public interface IExpiryServiceDiagnostics
   {
       bool IsEnabled { get; }
       DateTimeOffset? LastTickUtc { get; }
   }
   ```

2. **`PasswordExpiryNotificationService`** gains `IExpiryServiceDiagnostics` implementation:
   - Add `private DateTimeOffset? _lastTickUtc;` field (set atomically via `Interlocked` is not directly applicable to `DateTimeOffset` — use `volatile` + `lock` or encode as `long` ticks via `Interlocked.Exchange`)
   - Set it at the end of each `RunNotificationsAsync` call
   - `IsEnabled` returns `_notifSettings.Enabled`

3. **`ISmtpDiagnostics`** interface (new file):
   ```csharp
   public interface ISmtpDiagnostics
   {
       Task<(string Status, long LatencyMs, bool Skipped)> CheckAsync(CancellationToken ct);
   }
   ```
   Or simpler: let `HealthController` own the SMTP probe logic inline (like `CheckAdConnectivity()`), reading `SmtpSettings` from `IOptions<SmtpSettings>` and checking both enabled flags.

4. **`HealthController`** extended to inject `IOptions<SmtpSettings>`, `IOptions<EmailNotificationSettings>`, `IOptions<PasswordExpiryNotificationSettings>`, and `IExpiryServiceDiagnostics`. Returns the D-01 shape.

#### R-01 validation: TcpClient.ConnectAsync with CancellationToken on net10.0-windows

The existing `CheckAdConnectivity()` uses **synchronous** `TcpClient.Connect()` with no timeout — this is already a blocking risk on the current endpoint. D-05 says "synchronous on request thread with per-check timeouts."

`TcpClient.ConnectAsync(string host, int port, CancellationToken ct)` exists in .NET 5+ and honors cancellation via `CancellationToken`. On .NET 10 Windows, the overload `ConnectAsync(host, port, ct)` → `[VERIFIED: .NET 10 BCL — TcpClient.ConnectAsync(String, Int32, CancellationToken)]` is available. The correct pattern:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
using var client = new TcpClient();
await client.ConnectAsync(host, port, cts.Token);
```

**However:** D-05 says "synchronous on request thread." The health endpoint's `Get()` method is currently `IActionResult` (not async). To use `ConnectAsync`, `Get()` must become `async Task<IActionResult>`. This is a minor but required change. The alternative — `TcpClient.Connect()` with `SendTimeout`/`ReceiveTimeout` — does NOT apply to the connect phase; those timeouts only affect send/receive after connection. The correct approach is `ConnectAsync` + `CancellationTokenSource`.

**Risk R-01 status: VALIDATED — `ConnectAsync(host, port, CancellationToken)` works on net10.0-windows. `HealthController.Get()` must become `async Task<IActionResult>`.** [VERIFIED: .NET source/docs]

#### Existing AD health check blocking risk

The current sync `TcpClient.Connect(host, port)` has no timeout. For the AD probe, the same `ConnectAsync` + CTS pattern should replace the existing sync call simultaneously — this is a bug fix bundled into STAB-018.

---

### STAB-019 — Installer post-deploy verification

#### URL resolution (verified from Install-PassReset.ps1)

Lines 918–937 (STAB-001 output block):
```powershell
# Line 921:
Write-Ok "PassReset reachable at http://${hostHeader}:${selectedHttpPort}/"
# Line 937:
Write-Ok "PassReset reachable at https://${hostHeader}:${HttpsPort}/ (HTTPS binding configured)"
```

Variables `$hostHeader`, `$selectedHttpPort`, `$HttpsPort` are already in scope at this point in the script. The post-deploy block (D-09) can construct:
```powershell
$baseUrl = if ($HttpsPort) { "https://${hostHeader}:${HttpsPort}" } else { "http://${hostHeader}:${selectedHttpPort}" }
```

No new config knobs needed — directly references the same variables.

#### `-Force` mode constraint (from Phase 7 upstream)

Under `-Force`, the health check runs but does not prompt on failure — it just fails with exit code ≥1. This is already consistent with D-07.

#### Pester test coverage (R-05 validation)

**Glob for `*.Tests.ps1` under `deploy/` returned zero results.** No Pester tests exist. STAB-019 verification is manual UAT only. Do NOT add Pester toolchain in phase 10.

**Risk R-05 status: CONFIRMED — no Pester tests exist. Manual UAT only.**

---

### STAB-020 — CI security scans

#### `tests.yml` current structure (verified)

Single job `tests` with steps: checkout → setup-dotnet → setup-node → restore → backend tests → npm ci → frontend tests → upload coverage artifacts.

The workflow is `workflow_call` only (no `push`/`PR` triggers — those are in `ci.yml` which calls this). Adding a `security-audit` job as a second job in `tests.yml` keeps D-13 ("inside existing `tests.yml`").

The new job needs `needs: []` (no dependency on `tests` — can run in parallel) or `needs: [tests]` (sequential). Since it's a different concern and doesn't need build artifacts, `needs: []` is correct (parallel with `tests` job).

#### `dotnet list package --vulnerable` output format (R-02 validation)

**Verified from `--help` output:** No `--format json` flag exists. The command supports only `-v/--verbosity`, `-f/--framework`, `--include-transitive`. Output is plain text.

Example text output format:
```
Project 'PassReset.Web' has the following vulnerable packages
   [net10.0-windows]:
   Top-level Package      Requested   Resolved   Severity   Advisory URL
   > SomePackage          1.0.0       1.0.0      High       https://github.com/advisories/GHSA-xxx
```

**Parsing approach:** In the CI job (PowerShell on `windows-latest`), capture output and use `Select-String` to find lines with "High" or "Critical" (case-insensitive). Cross-reference advisory IDs against `deploy/security-allowlist.json`.

Advisory ID extraction: the URL ends with `GHSA-xxxx-xxxx-xxxx` — parse with regex `GHSA-[a-z0-9-]+`.

**Risk R-02 status: VALIDATED — no JSON flag. Text parsing with PowerShell `Select-String` + regex is the correct approach.**

#### `npm audit --json` schema (R-03 validation)

CI uses Node 22 (from `tests.yml` line `node-version: '22'`). npm version on this machine: 11.11.1. npm audit JSON schema has been stable since npm 7 (schema "auditReportVersion": 2). In npm 7+, the JSON output structure is:

```json
{
  "auditReportVersion": 2,
  "vulnerabilities": {
    "package-name": {
      "name": "...",
      "severity": "high|critical|moderate|low|info",
      "via": [...],
      "effects": [...],
      "fixAvailable": true|false|{...}
    }
  },
  "metadata": { "vulnerabilities": { "high": N, "critical": N, ... } }
}
```

The `metadata.vulnerabilities` object is the fast path — check `high + critical > 0` minus allowlisted advisories.

Advisory IDs live in `via[].url` (for root advisories) in format `https://github.com/advisories/GHSA-xxx`.

**Risk R-03 status: VALIDATED — use `npm audit --json`, parse `metadata.vulnerabilities.{high,critical}` for fast check, parse `vulnerabilities[].via[].url` for allowlist matching.**

#### `deploy/security-allowlist.json` schema

New file, starts with comment template. Since JSON doesn't support comments natively, use a `_comment` key per entry or a top-level `_readme` string. Recommended shape:
```json
{
  "_readme": "Add entries to suppress known advisories. Entries expire after 90 days — re-review required.",
  "advisories": [
    {
      "id": "GHSA-xxxx-xxxx-xxxx",
      "rationale": "...",
      "expires": "2026-07-20",
      "scope": "npm|nuget"
    }
  ]
}
```

---

### STAB-021 — Password policy panel visibility

#### Current state (verified from source)

**`ClientSettings.cs` line 31:** `public bool ShowAdPasswordPolicy { get; set; } = false;` — defaults to `false`.

**`appsettings.schema.json` line 246:** `"ShowAdPasswordPolicy": { "type": "boolean", "default": false }` — schema default also `false`.

**`PasswordForm.tsx` lines 96–98 + 335:**
- Hook: `usePolicy(settings.showAdPasswordPolicy === true)` — only fetches when setting is `true`
- Render: panel is inside `{settings.showAdPasswordPolicy && (...)}` guard
- **Current position in form:** After CurrentPassword field (line 335), before NewPassword field

**`AdPasswordPolicyPanel.tsx`** (entire file read):
- No disclosure/accordion/collapse widget — already renders inline as a `<Paper variant="outlined">`
- `role="region"` + `aria-label="Password requirements"` — already accessible
- No `defaultExpanded` needed — it's not collapsed; it just renders or doesn't
- D-19 mentions `<Collapse defaultExpanded>` — this may not be needed since the panel isn't in a `<Collapse>` today

**`/api/password/policy` endpoint (PasswordController.cs line 91):**
```csharp
if (!_clientSettings.Value.ShowAdPasswordPolicy) return NotFound();
```
The endpoint is `[HttpGet("policy")]` with no `[Authorize]` attribute — it is **anonymous**. When `ShowAdPasswordPolicy=true`, it returns the policy; when `false`, 404. The `usePolicy` hook only calls it when `enabled=true`. No auth change needed.

#### What STAB-021 actually requires

D-16: "make the panel visible by default (not behind a disclosure widget if one exists) and ensure it appears above the password fields."

Reading the form order: Username → CurrentPassword → **PolicyPanel** (line 335) → NewPassword → HIBP → Clipboard → StrengthMeter → NewPasswordVerify → Submit.

"Above the password fields" = above the username field, OR above the new-password field. Since the requirement is about the password *change* fields (current + new), placing it above Username makes the most sense for "before they attempt a change."

**Changes needed:**
1. `appsettings.json`: Change `ShowAdPasswordPolicy` default to `true` (or add it with `true`)
2. `PasswordForm.tsx`: Move `<AdPasswordPolicyPanel>` render block above the Username `<TextField>` (before line 301)
3. The `settings.showAdPasswordPolicy` guard stays — operators can still disable it
4. No server changes (D-18 confirmed)

**R-04 status: VALIDATED — `/api/password/policy` is anonymous-friendly. STAB-021 is purely layout + default-value change. No `<Collapse>` needed (panel is not collapsed today).**

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| TCP connect timeout | Custom socket loop | `TcpClient.ConnectAsync(host, port, CancellationToken)` | BCL handles cancellation correctly; avoids sync-over-async trap |
| npm audit severity parsing | Custom parser | `npm audit --json` + `metadata.vulnerabilities` | Stable schema since npm 7; fast path via metadata object |
| dotnet vuln severity parsing | JSON output | `Select-String` regex on text output | No `--format json` flag exists — text parsing is the only path |
| Allowlist expiry enforcement | Date logic | ISO 8601 string comparison in PS/bash | Simple string compare works; no date library needed |

---

## Common Pitfalls

### Pitfall 1: Breaking existing health response consumers
**What goes wrong:** Changing `checks.ad` from `"ok"/"unreachable"` string to a structured object breaks load-balancer scripts checking that field.
**Why it happens:** D-01 says "additive" but the existing `ad` value is a string, not an object.
**How to avoid:** The existing `status` top-level field is what load balancers check. The `checks` sub-object shape can change — it's diagnostic detail. Verify no downstream consumer pattern-matches on `checks.ad == "ok"` specifically. The constraint is that `status: "healthy"/"degraded"/"unhealthy"` must remain stable.
**Warning signs:** Any integration test asserting `checks.ad` string value.

### Pitfall 2: ExpiryService not registered in debug mode
**What goes wrong:** `IExpiryServiceDiagnostics` injected into `HealthController` but not registered when `UseDebugProvider=true` → DI exception at startup.
**Why it happens:** `PasswordExpiryNotificationService` is only registered when `expirySettings.Enabled && !UseDebugProvider` (Program.cs line 169–170).
**How to avoid:** Register a `NullExpiryServiceDiagnostics` (IsEnabled=false, LastTickUtc=null) as fallback in the debug branch. Mirror the null-object pattern.

### Pitfall 3: `HealthController.Get()` sync vs async
**What goes wrong:** `TcpClient.ConnectAsync` called inside a sync `IActionResult Get()` via `.GetAwaiter().GetResult()` → thread-pool starvation risk under load.
**How to avoid:** Change `Get()` to `async Task<IActionResult> GetAsync()`. ASP.NET Core routing handles both; the `[HttpGet]` attribute doesn't care about the method name convention.

### Pitfall 4: `npm audit` exit code ≠ vulnerability presence
**What goes wrong:** `npm audit` exits non-zero when any vulnerability exists — CI step fails before the allowlist check runs.
**How to avoid:** Run `npm audit --json || true` (capture output regardless of exit code), then parse the JSON and apply allowlist logic before deciding to fail.

### Pitfall 5: Panel position in PasswordForm
**What goes wrong:** Moving `<AdPasswordPolicyPanel>` above Username causes `usePolicy` to fire before `settings` is confirmed loaded.
**Why it happens:** `usePolicy` depends on `settings.showAdPasswordPolicy`; `settings` is passed as a prop so it's always defined when `PasswordForm` renders — no race condition.
**How to avoid:** No change needed; confirm `settings` is always defined at `PasswordForm` render time (it is — App.tsx renders `<PasswordForm settings={settings}>` only after `useSettings()` resolves).

---

## Code Examples

### IExpiryServiceDiagnostics — modeled on ILockoutDiagnostics
```csharp
// Source: LockoutPasswordChangeProvider.cs:12-16 (existing pattern)
public interface IExpiryServiceDiagnostics
{
    bool IsEnabled { get; }
    DateTimeOffset? LastTickUtc { get; }
}
```

### LastTick atomic update in PasswordExpiryNotificationService
```csharp
// Add field:
private long _lastTickTicks; // 0 = never run

// At end of RunNotificationsAsync:
Interlocked.Exchange(ref _lastTickTicks, DateTimeOffset.UtcNow.UtcTicks);

// IExpiryServiceDiagnostics impl:
public DateTimeOffset? LastTickUtc =>
    _lastTickTicks == 0 ? null : new DateTimeOffset(Interlocked.Read(ref _lastTickTicks), TimeSpan.Zero);
```

### SmtpDiagnostics inline in HealthController (simpler than a new interface)
```csharp
// Source: D-02 decision + TcpClient.ConnectAsync pattern
private async Task<(string status, long latencyMs, bool skipped)> CheckSmtpAsync(
    SmtpSettings smtp, bool emailEnabled, bool expiryEnabled)
{
    if (!emailEnabled && !expiryEnabled)
        return ("skipped", 0, true);

    var sw = Stopwatch.StartNew();
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, cts.Token);
        return ("healthy", sw.ElapsedMilliseconds, false);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "SMTP health check failed ({Host}:{Port})", smtp.Host, smtp.Port);
        return ("unhealthy", sw.ElapsedMilliseconds, false);
    }
}
```

### npm audit CI snippet
```yaml
- name: npm audit
  working-directory: src/PassReset.Web/ClientApp
  shell: pwsh
  run: |
    $audit = npm audit --json 2>&1 | Out-String
    $result = $audit | ConvertFrom-Json
    $allowlist = (Get-Content deploy/security-allowlist.json | ConvertFrom-Json).advisories
    $today = [datetime]::UtcNow.ToString("yyyy-MM-dd")
    $validAllowlist = $allowlist | Where-Object { $_.expires -gt $today -and $_.scope -eq "npm" } | Select-Object -ExpandProperty id
    $highCrit = $result.vulnerabilities.PSObject.Properties.Value |
      Where-Object { $_.severity -in @("high","critical") } |
      Where-Object { $_.via | Where-Object { $_ -is [psobject] } |
        ForEach-Object { ($_.url -replace ".*/","") } |
        Where-Object { $_ -notin $validAllowlist } }
    if ($highCrit) { Write-Error "Unfixed high/critical npm advisories found"; exit 1 }
```

### dotnet vulnerable CI snippet
```yaml
- name: dotnet audit
  shell: pwsh
  run: |
    $output = dotnet list src/PassReset.sln package --vulnerable --include-transitive 2>&1
    $allowlist = (Get-Content deploy/security-allowlist.json | ConvertFrom-Json).advisories
    $today = [datetime]::UtcNow.ToString("yyyy-MM-dd")
    $validAllowlist = $allowlist | Where-Object { $_.expires -gt $today -and $_.scope -eq "nuget" } | Select-Object -ExpandProperty id
    $highLines = $output | Select-String -Pattern "(High|Critical)" -CaseSensitive:$false
    $unfixed = $highLines | Where-Object {
      $advisory = [regex]::Match($_, "GHSA-[a-z0-9\-]+").Value
      $advisory -and $advisory -notin $validAllowlist
    }
    if ($unfixed) { $unfixed | ForEach-Object { Write-Warning $_ }; exit 1 }
```

### Installer post-deploy block skeleton
```powershell
if (-not $SkipHealthCheck) {
    $baseUrl = if ($certThumbprintResolved) {
        "https://${hostHeader}:${HttpsPort}"
    } else {
        "http://${hostHeader}:${selectedHttpPort}"
    }
    $maxAttempts = 10; $attempt = 0; $ok = $false
    do {
        Start-Sleep -Seconds 2; $attempt++
        try {
            $h = Invoke-WebRequest -Uri "$baseUrl/api/health" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            $s = Invoke-WebRequest -Uri "$baseUrl/api/password" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($h.StatusCode -eq 200 -and $s.StatusCode -eq 200) { $ok = $true }
        } catch { Write-Warning "Attempt ${attempt}: $_" }
    } while (-not $ok -and $attempt -lt $maxAttempts)
    if (-not $ok) {
        Write-Error "Post-deploy health check failed after ${maxAttempts} attempts. Last response: ..."
        exit 1
    }
    Write-Ok "Health OK — AD: $($body.checks.ad.status), SMTP: $($body.checks.smtp.status), ExpiryService: $($body.checks.expiryService.status)"
}
```

---

## Validation Architecture

| Req ID | Behavior | Test Type | File | Pass Criteria |
|--------|----------|-----------|------|---------------|
| STAB-018 | `/api/health` returns 200 with `checks.ad.status` | Unit (xUnit) | `src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs` | `response.checks.ad.status == "healthy"` |
| STAB-018 | SMTP skipped when both email features disabled | Unit (xUnit) | `HealthControllerTests.cs` | `checks.smtp.skipped == true` |
| STAB-018 | ExpiryService reports healthy when Enabled=false | Unit (xUnit) | `HealthControllerTests.cs` | `checks.expiryService.status == "not-enabled"` (or "healthy") |
| STAB-018 | AD unhealthy → HTTP 503 | Unit (xUnit) | `HealthControllerTests.cs` | `StatusCode == 503` |
| STAB-018 | LastTick atomic read thread safety | Unit | `PasswordExpiryNotificationServiceTests.cs` | Interlocked.Read returns consistent value |
| STAB-019 | Post-deploy block retries 10× then exits ≥1 | Manual UAT | n/a — no Pester | Operator runs install on test VM; confirms exit code on bad health |
| STAB-019 | `-SkipHealthCheck` bypasses check | Manual UAT | n/a | Install completes with switch; no health call made |
| STAB-020 | High/critical npm advisory fails CI | Integration (CI) | `.github/workflows/tests.yml` | Job exit code ≠ 0 when unfixed high advisory |
| STAB-020 | Allowlisted advisory does not fail CI | Integration (CI) | `tests.yml` + `deploy/security-allowlist.json` | Job passes when advisory in allowlist with future expiry |
| STAB-020 | Expired allowlist entry still fails | Integration (CI) | `tests.yml` | Past `expires` date → advisory treated as unfixed |
| STAB-021 | Panel renders above username field by default | RTL unit | `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.test.tsx` | `getByRole("region", { name: /password requirements/i })` appears before first `<input>` in DOM |
| STAB-021 | Panel hidden when `showAdPasswordPolicy=false` | RTL unit | `AdPasswordPolicyPanel.test.tsx` | `queryByRole("region")` returns null |

### Wave 0 Gaps

- [ ] `src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs` — new file, covers STAB-018 unit tests
- [ ] `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.test.tsx` — covers STAB-021 rendering (may already exist — verify before Wave 0)
- [ ] `deploy/security-allowlist.json` — new file with empty advisories array

---

## Environment Availability

Step 2.6: SKIPPED — this phase is code/config changes only. No new external runtimes or services introduced. All dependencies (AD, SMTP, npm, dotnet SDK) are already present in the project's CI and runtime environment.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `TcpClient.ConnectAsync(string, int, CancellationToken)` honors the token deadline on net10.0-windows | STAB-018 R-01 | SMTP/AD probe could block IIS thread if unresponsive host encountered |
| A2 | `npm audit --json` schema `metadata.vulnerabilities` object is stable across npm 10/11 | STAB-020 R-03 | CI parsing script breaks; fallback: parse vulnerability count from `npm audit` text output |
| A3 | `$hostHeader` and `$selectedHttpPort`/`$HttpsPort` are in-scope at the STAB-001 print block and remain accessible for the new post-deploy block | STAB-019 | Script would need new URL-detection logic; low risk since variables are script-level |

---

## Open Questions

1. **Should `AdPasswordPolicyPanel` move above Username or stay after CurrentPassword?**
   - D-16 says "above the password fields" — ambiguous whether Username counts as a "field"
   - Current position (after CurrentPassword, before NewPassword) could satisfy "above the new password field"
   - Recommendation: move to top of form (above Username) for clearest UX — user sees requirements before typing anything

2. **`IExpiryServiceDiagnostics` registration when `UseDebugProvider=true`**
   - Service not registered in debug mode — need explicit null-object registration
   - Planner should include this as a task in 10-01 plan

---

## Sources

### Primary (HIGH confidence)
- `src/PassReset.Web/Controllers/HealthController.cs` — full file read; existing check shape and ILockoutDiagnostics pattern
- `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs` — full interface definition verified
- `src/PassReset.Web/Program.cs` — DI registration pattern verified at lines 140–179
- `src/PassReset.Web/Services/PasswordExpiryNotificationService.cs` — full file; no LastTick field confirmed
- `src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.tsx` — full file; panel structure confirmed
- `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` — full file; panel position at line 335 confirmed
- `src/PassReset.Web/Controllers/PasswordController.cs` — policy endpoint anonymous, `ShowAdPasswordPolicy` default false
- `.github/workflows/tests.yml` — full file; job structure confirmed; no `--format json` on dotnet confirmed
- `deploy/Install-PassReset.ps1` — URL vars at lines 921/937; no Pester tests confirmed
- `tasks/lessons.md` — parallel-wave fallout documented; D-20 sequential execution confirmed

### Secondary (MEDIUM confidence)
- `dotnet list package --vulnerable --help` (run locally) — no `--format json` flag confirmed [VERIFIED: local SDK]
- npm 11.11.1 local install — npm audit JSON schema v2 confirmed stable [VERIFIED: local npm]

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries are already in use; no new dependencies
- Architecture: HIGH — interfaces modeled directly on existing ILockoutDiagnostics pattern
- Pitfalls: HIGH — most pitfalls derived from direct source reading, not inference
- CI parsing: MEDIUM — text regex parsing of dotnet output is brittle; exact column positions may differ between SDK versions

**Research date:** 2026-04-20
**Valid until:** 2026-05-20 (stable domain; net10 SDK output format unlikely to change in 30 days)
