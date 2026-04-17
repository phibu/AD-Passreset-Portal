# Phase 9: Security Hardening — Research

**Researched:** 2026-04-17
**Domain:** ASP.NET Core 10 security hardening (error-response redaction, WebApplicationFactory integration tests, RFC 5424 structured-data audit, HTTPS/HSTS, env-var secrets)
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01 (STAB-013):** Collapse **only** `InvalidCredentials` and `UserNotFound` to a single production response. All other codes retain specific `ApiErrorCode` values.
- **D-02 (STAB-013):** Use existing `ApiErrorCode.Generic` (0). **Do not** add a new `AuthenticationFailed` enum member — keeps `EnumMemberCount_LocksInKnownSurface` at 20, avoids TypeScript mirror update.
- **D-03 (STAB-013):** Toggle is environment-based via `IHostEnvironment.IsProduction()`. No `WebSettings` config flag.
- **D-04 (STAB-013):** Reuse existing "The credentials you supplied are not valid." message. No new i18n key.
- **D-05 (STAB-013):** SIEM remains granular — `MapErrorCodeToSiemEvent` is **not** modified. Only the wire response collapses.
- **D-06 (STAB-014):** Tests use `WebApplicationFactory<Program>`. Program.cs already exposes `public partial class Program { }`.
- **D-07 (STAB-014):** Four scenarios — rate-limit enforced, rate-limit bypass-path-if-any, reCAPTCHA enabled+invalid, reCAPTCHA disabled.
- **D-08 (STAB-014):** Test-only stub — reuse `DebugPasswordChangeProvider`.
- **D-09 (STAB-015):** Extend `SiemService` with RFC 5424 structured-data elements; single audit sink.
- **D-10 (STAB-015):** Allowlist DTO `AuditEvent(EventType, Outcome, Username, ClientIp, TraceId, Detail)` — no password/token fields exist on the DTO. Compile-time redaction.
- **D-11 (STAB-015):** Audit existing 10 `SiemEventType` members against STAB-015 categories; **do not** pre-emptively add `AttemptStarted`.
- **D-12 (STAB-016):** Keep app-level `UseHttpsRedirection()` + HSTS `max-age=31536000; includeSubDomains`. **No preload.**
- **D-13 (STAB-016):** Installer binding check — warn-not-block using Phase 7 `Write-Warn`. No auto-delete bindings.
- **D-14 (STAB-016):** Keep `WebSettings.EnableHttpsRedirect` config knob (reverse-proxy escape hatch).
- **D-15 (STAB-017):** In-scope secrets — `SmtpSettings.Password`, `PasswordChangeOptions.ServiceAccountPassword` (if present), `ClientSettings.Recaptcha.PrivateKey`. SMTP host / LDAP domain stay in appsettings.
- **D-16 (STAB-017):** Default `__` env-var convention (`SmtpSettings__Password`, etc.). Zero custom code — `ConfigurationBuilder.AddEnvironmentVariables()` already present.
- **D-17 (STAB-017):** `dotnet user-secrets` for dev — `AddUserSecrets()` already active in Development via ASP.NET Core defaults.
- **D-18 (STAB-017):** Installer does **not** set env vars. Documentation only.

### Claude's Discretion

- Exact RFC 5424 SD-ID + param naming for audit structured-data.
- Whether `ClientSettings.Recaptcha.PrivateKey` needs additional validator rules under env-var sourcing.
- Test class naming / file organization within `PassReset.Tests`.
- Specific wording of installer `Write-Warn` binding-check message.
- Whether `Program.cs` gets a helper method for the IsProduction collapse or it's inlined at controller level.

### Deferred Ideas (OUT OF SCOPE)

- DPAPI / encrypted-at-rest secrets (→ v2.0 Phase 13 / V2-003).
- HSTS preload (operator policy territory).
- `AuthenticationFailed` dedicated error code.
- Custom `PASSRESET_` env-var prefix.
- Installer auto-injection of AppPool env vars.
- Parallel Serilog audit sink.
- Removing `WebSettings.EnableHttpsRedirect`.

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| STAB-013 | Production responses do not distinguish `InvalidCredentials` from `UserNotFound` (SIEM still does) | Technical Approach §1: single-point collapse at `BadRequest(result)` in `PasswordController.PostAsync`, gated by `IHostEnvironment.IsProduction()` injected via constructor. SIEM path (`MapErrorCodeToSiemEvent`) untouched. |
| STAB-014 | Rate-limit + reCAPTCHA enforcement covered by integration tests (both enabled and disabled paths) | Technical Approach §2: `WebApplicationFactory<Program>` pattern already established in `PasswordControllerTests.DebugFactory` — new test class reuses `ConfigureAppConfiguration` with `AddInMemoryCollection` for per-test config flips. Rate limiter state isolated by per-test factory (existing pattern). |
| STAB-015 | Structured audit events covering attempts, failures, rate-limit blocks, successes — strict redaction | Technical Approach §3: RFC 5424 SD-ID syntax, `AuditEvent` DTO shape, coverage audit of existing 10 `SiemEventType` members (no gap — reuse all). |
| STAB-016 | HTTPS redirect + HSTS + no accidental plain-HTTP binding | Technical Approach §4: existing `UseHttpsRedirection()` + HSTS intact. Installer binding check uses `Get-WebBinding -Name $SiteName -Protocol https` — existing installer already calls this for cert binding; reuse pattern. |
| STAB-017 | SMTP / LDAP / reCAPTCHA secrets via env vars or user-secrets | Technical Approach §5: Default ASP.NET Core `__` binding. `appcmd` syntax for AppPool env vars. `dotnet user-secrets` commands. |

</phase_requirements>

## Summary

Phase 9 is a hardening phase with **five narrowly scoped, low-ambiguity changes** riding on infrastructure that already exists. The controller already maps error codes granularly and emits SIEM events separately from the wire response — STAB-013 is a single `if (env.IsProduction()) collapse(code)` gate at the `BadRequest(result)` edge. STAB-014 tests follow the exact pattern already shipped in `PasswordControllerTests.DebugFactory` (per-test `WebApplicationFactory` subclass, `AddInMemoryCollection` config override, `DebugPasswordChangeProvider` via `WebSettings:UseDebugProvider=true`). STAB-015 extends the existing pure `SiemSyslogFormatter.Format` helper to emit an RFC 5424 STRUCTURED-DATA element — compile-time redaction achieved via an `AuditEvent` record with no `Password`/`Token` fields. STAB-016 adds one PowerShell function to `Install-PassReset.ps1` that reuses existing `Get-WebBinding` calls and `Write-Warn` helper. STAB-017 is a zero-code change — `AddEnvironmentVariables()` is already active; deliverables are documentation and an end-to-end smoke proof. The 10-member `SiemEventType` enum already covers STAB-015's categories without addition.

**Primary recommendation:** Implement STAB-013 as a 3-line `if (env.IsProduction()) MapInvalidToGeneric(result)` helper called from the two code paths that bind `InvalidCredentials`/`UserNotFound` error items (one of which is the provider-error branch at line 191-198 of `PasswordController.PostAsync`). STAB-014 uses one new test class per scenario family, each with its own factory subclass and distinct `RemoteIpAddress` to avoid rate-limiter partition collisions. STAB-015 uses SD-ID `passreset@32473` (per RFC 5424 §7.2.2 private-enterprise convention). STAB-017 ships as pure docs + a single integration smoke test proving env-var binding precedence over `appsettings.json`.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Error-code collapse (STAB-013) | API Controller | — | Serialization edge; SIEM granularity lives upstream of collapse. |
| Integration test coverage (STAB-014) | Test Harness (out-of-process via `WebApplicationFactory`) | Configuration Provider (`AddInMemoryCollection`) | Exercises full middleware pipeline (rate-limiter + reCAPTCHA) without touching AD. |
| Structured audit (STAB-015) | Service Layer (`SiemService`) | Formatter (`SiemSyslogFormatter` pure static) | Keep SIEM the single audit sink; compile-time DTO prevents leak-by-accident. |
| HTTPS redirect / HSTS (STAB-016) | ASP.NET Core middleware (app) | PowerShell installer (binding check) | Belt-and-suspenders: app-level redirect for runtime + installer check for operator misconfiguration. |
| Env-var secrets (STAB-017) | Configuration provider (host builder) | IIS AppPool (operator concern) + `dotnet user-secrets` (dev) | ASP.NET Core default `AddEnvironmentVariables()` already handles binding; the change is operational doctrine, not code. |

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Microsoft.AspNetCore.Mvc.Testing | 10.0.x (tracks TFM) | `WebApplicationFactory<TEntryPoint>` integration test host | ASP.NET Core canonical pattern; already in `PassReset.Tests.csproj` (evidenced by existing tests). [VERIFIED: codebase `PasswordControllerTests.cs` uses it] |
| Microsoft.Extensions.Configuration.EnvironmentVariables | 10.0.x | `AddEnvironmentVariables()` with `__` path delimiter | Default in `Host.CreateApplicationBuilder`. [VERIFIED: codebase + ASP.NET Core docs] |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.x | `dotnet user-secrets` backing store | Default in Development when `UserSecretsId` present in csproj. [CITED: learn.microsoft.com/aspnet/core/security/app-secrets] |
| xUnit v3 | existing | Test runner | Already pinned. [VERIFIED: solution uses it] |
| IIS WebAdministration module | built-in | `Get-WebBinding` / `New-WebBinding` | Already imported by `Install-PassReset.ps1`. [VERIFIED: installer line 803, 887, 901] |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `appcmd.exe` | Windows/IIS built-in | AppPool env var configuration (operator runs this) | STAB-017 docs in `docs/IIS-Setup.md`. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `WebApplicationFactory` per-test | Shared `IClassFixture` | Per-test isolates rate-limiter state. `IClassFixture` would share the in-memory fixed-window partition and create order-dependent tests. Reject. |
| RFC 5424 SD-ID | Single JSON blob in MSG field | SD-ID is the protocol-native audit channel; SIEM parsers (Splunk, QRadar, Graylog) natively index SD-PARAMs. [CITED: RFC 5424 §6.3] |
| Custom env-var prefix `PASSRESET_*` | Default `__` convention | Custom prefix requires `AddEnvironmentVariables("PASSRESET_")` change. D-16 rejects it. |

**Installation:** None — all dependencies already present. Phase ships zero NuGet / npm changes.

**Version verification:**
- `dotnet user-secrets` is a built-in global tool post-.NET 3.0; no `dotnet tool install` needed. [CITED: learn.microsoft.com/dotnet/core/tools/dotnet-user-secrets]
- `Microsoft.AspNetCore.Mvc.Testing` tracks the project TFM; no explicit version pin needed — it's in the ASP.NET Core metapackage shipped with .NET 10.

## Architecture Patterns

### System Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Browser → HTTPS (IIS :443) → ASP.NET Core module → Kestrel in-process      │
│                                     │                                       │
│                                     ▼                                       │
│ [UseHttpsRedirection if EnableHttpsRedirect]  ←── STAB-016 (unchanged)     │
│ [UseHsts when !Development]                                                │
│ [Rate limiter: password-fixed-window, 5/5min per IP]  ←── STAB-014 tests   │
│                                     │                                       │
│                                     ▼                                       │
│ POST /api/password → PasswordController.PostAsync                          │
│   │                                                                         │
│   ├── ModelState.IsValid?                                                  │
│   ├── Levenshtein distance check                                           │
│   ├── reCAPTCHA if Enabled && PrivateKey set   ←── STAB-014 tests          │
│   │                                                                         │
│   ├── IPasswordChangeProvider.PerformPasswordChangeAsync()                 │
│   │     (LockoutDecorator → DebugProvider in tests; AD in prod)            │
│   │                                                                         │
│   └── Error path:                                                          │
│         ├── Audit() → ISiemService.LogEvent(siemType, user, ip, detail)    │
│         │                        │                                          │
│         │                        ▼                                          │
│         │     SiemSyslogFormatter.Format(..., structuredData) ←── STAB-015 │
│         │     → RFC 5424 datagram → UDP/TCP → syslog collector             │
│         │                                                                   │
│         └── return BadRequest(result)  ← STAB-013 collapse gate here       │
│                         IF IsProduction && code ∈ {InvalidCreds,UserNotFound}│
│                         THEN replace error with ApiErrorCode.Generic        │
│                                                                             │
│ Configuration sources (precedence low→high):                               │
│   1. appsettings.json                                                      │
│   2. appsettings.{Environment}.json                                        │
│   3. dotnet user-secrets (Development only)  ←── STAB-017                  │
│   4. AddEnvironmentVariables() with __ delimiter  ←── STAB-017             │
│   5. Command-line args                                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Recommended Project Structure
```
src/PassReset.Web/
├── Controllers/PasswordController.cs        # STAB-013 collapse at PostAsync tail
├── Services/
│   ├── SiemService.cs                       # STAB-015 accepts AuditEvent
│   ├── SiemSyslogFormatter.cs               # STAB-015 emits SD-ELEMENT
│   └── AuditEvent.cs                        # NEW: allowlist DTO (record)
├── Program.cs                               # no change for STAB-016/017; existing UseHsts + AddEnvironmentVariables suffice
deploy/Install-PassReset.ps1                 # STAB-016 new Test-HttpsBinding function
src/PassReset.Tests/Web/Controllers/
├── PasswordControllerTests.cs               # existing
├── PasswordControllerRateLimitTests.cs      # NEW STAB-014 (a, b)
├── PasswordControllerRecaptchaTests.cs      # NEW STAB-014 (c, d)
└── PasswordControllerRedactionTests.cs      # NEW STAB-013 gate (Production environment)
docs/
├── IIS-Setup.md                             # STAB-017 appcmd section added
├── Secret-Management.md                     # STAB-017 user-secrets + env-var workflow
├── appsettings-Production.md                # STAB-017 env-var override table
├── Known-Limitations.md                     # STAB-013 wire-vs-SIEM note
CONTRIBUTING.md                              # STAB-017 dev workflow
```

### Pattern 1: Per-Test WebApplicationFactory Subclass (STAB-014)

**What:** Each test scenario gets a dedicated `WebApplicationFactory<Program>` subclass that overrides `ConfigureAppConfiguration` to flip the exact knobs under test.

**When to use:** When scenarios require mutually exclusive config (reCAPTCHA enabled vs. disabled; rate-limit on vs. off).

**Example:** (pattern extracted from existing `PasswordControllerTests.DebugFactory`)
```csharp
// Source: src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs (existing pattern)
public sealed class RecaptchaEnabledFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // still Dev so SIEM/email remain off
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSettings:UseDebugProvider"]        = "true",
                ["WebSettings:EnableHttpsRedirect"]     = "false",
                ["ClientSettings:MinimumDistance"]      = "0",
                ["ClientSettings:Recaptcha:Enabled"]    = "true",
                ["ClientSettings:Recaptcha:PrivateKey"] = "test-key-expected-to-fail-remote-verify",
                ["ClientSettings:Recaptcha:FailOpenOnUnavailable"] = "false",
                ["EmailNotificationSettings:Enabled"]   = "false",
                ["SiemSettings:Syslog:Enabled"]         = "false",
                ["PasswordChangeOptions:PortalLockoutThreshold"] = "0",
                ["PasswordChangeOptions:UseAutomaticContext"]    = "true",
            });
        });
    }
}
```
Call the remote Google verify endpoint? **No.** Override `_recaptchaHttp` is infeasible (static). Instead, supply a syntactically valid but semantically invalid token — Google returns `success=false` and the controller path returns `InvalidCaptcha`. If CI must be offline, the planner should add a test-only `IRecaptchaVerifier` abstraction in a separate refactor — but D-07 does not require that. [ASSUMED: existing static HttpClient design won't be mocked in this phase; confirm with user before planning a refactor.]

### Pattern 2: Rate-limiter partition isolation (STAB-014a)

**What:** Microsoft.AspNetCore.RateLimiting partitions by IP via `PartitionedRateLimiter.CreateChained` plus `HttpContext.Connection.RemoteIpAddress`. `WebApplicationFactory` uses `TestServer` which normally reports `127.0.0.1` for RemoteIpAddress, so all tests share the same partition unless explicitly changed.

**When to use:** STAB-014(a) — "6th request in window returns 429".

**Example:**
```csharp
// Pattern for test rate-limit partition isolation
// Set a custom X-Forwarded-For or override the connection IP via DefaultRequestHeaders
// BUT: TestServer ignores X-Forwarded-For unless ForwardedHeaders middleware is added.
// Simplest approach: per-test factory = fresh in-memory rate limiter = no partition leak.

[Fact]
public async Task Post_SixthRequestInWindow_Returns429()
{
    using var factory = new DebugFactory(); // fresh state per test
    var client = factory.CreateClient();
    for (int i = 0; i < 5; i++)
        (await client.PostAsJsonAsync("/api/password", MakeRequest("anyuser"))).EnsureSuccessStatusCode();
    var sixth = await client.PostAsJsonAsync("/api/password", MakeRequest("anyuser"));
    Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
}
```
**Confirmation:** The existing `PasswordControllerTests.DebugFactory` already relies on per-test factory disposal (`public void Dispose() => _factory.Dispose()`) — STAB-014 can reuse this pattern. [VERIFIED: codebase line 21-23]

### Pattern 3: Compile-Time Redaction via Allowlist DTO (STAB-015)

**What:** Introduce `record AuditEvent(string EventType, string Outcome, string Username, string? ClientIp, string? TraceId, string? Detail)`. No `Password`, `NewPassword`, `CurrentPassword`, `Token`, `PrivateKey` properties exist. A compile error results if anyone tries to assign a password into an audit event.

**When to use:** STAB-015 redaction guarantee.

**Example:**
```csharp
// Source: new file src/PassReset.Web/Services/AuditEvent.cs
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
`ISiemService.LogEvent` gains an overload `LogEvent(AuditEvent evt)`; legacy signature stays for compatibility. [VERIFIED: D-10 locks this shape]

### Pattern 4: RFC 5424 Structured-Data Element (STAB-015)

**What:** RFC 5424 §6.3 defines a STRUCTURED-DATA field between HEADER and MSG:
```
<PRI>VERSION TIMESTAMP HOSTNAME APP-NAME PROCID MSGID [SD-ID SD-PARAM...] MSG
```

SD-ID format: `name@<PEN>` (Private Enterprise Number) or IANA-registered. For private audit fields, use the `@32473` convention (IANA's reserved PEN for examples) or register an IANA PEN. [CITED: RFC 5424 §7.2.2]

**Syntax rules:**
- SD-ID: `printusascii` minus `=`, space, `]`, `"`. Length 1-32 chars.
- SD-PARAM name: same character class, length 1-32.
- SD-PARAM value: wrapped in double quotes; escape `"`, `\`, `]` as `\"`, `\\`, `\]`.
- Example: `[passreset@32473 outcome="Success" user="alice" ip="10.0.0.5" traceId="abc123"]`

**When to use:** STAB-015 — extend `SiemSyslogFormatter.Format` signature to accept an optional structured-data object.

**Example:**
```csharp
// Source: RFC 5424 §6.3; adapted for SiemSyslogFormatter
internal static string FormatStructuredData(AuditEvent evt)
{
    string Esc(string s) => s
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("]",  "\\]",  StringComparison.Ordinal);

    var sb = new StringBuilder("[passreset@32473");
    sb.Append(" outcome=\"").Append(Esc(evt.Outcome)).Append('"');
    sb.Append(" eventType=\"").Append(evt.EventType).Append('"');
    sb.Append(" user=\"").Append(Esc(evt.Username)).Append('"');
    if (evt.ClientIp is { Length: > 0 }) sb.Append(" ip=\"").Append(Esc(evt.ClientIp)).Append('"');
    if (evt.TraceId  is { Length: > 0 }) sb.Append(" traceId=\"").Append(Esc(evt.TraceId)).Append('"');
    if (evt.Detail   is { Length: > 0 }) sb.Append(" detail=\"").Append(Esc(evt.Detail)).Append('"');
    sb.Append(']');
    return sb.ToString();
}
```
[CITED: RFC 5424 §6.3, §7.2.2]

### Pattern 5: Environment-based behavior gate (STAB-013)

**What:** Inject `IHostEnvironment` into `PasswordController`, gate collapse at the error-return edge.

**Example:**
```csharp
// Source: ASP.NET Core docs — IHostEnvironment is auto-registered in DI
public PasswordController(
    IPasswordChangeProvider provider,
    /* ... existing deps ... */,
    IHostEnvironment hostEnvironment,
    ILogger<PasswordController> logger) { /* assign field */ }

// In PostAsync error branch (was line 191-198):
if (error is not null)
{
    var siemType = MapErrorCodeToSiemEvent(error.ErrorCode);
    Audit($"Failed:{error.ErrorCode}", model.Username, clientIp, siemType, error.Message);

    // STAB-013: collapse account-enumeration codes in production
    var wireError = _hostEnvironment.IsProduction() && IsAccountEnumerationCode(error.ErrorCode)
        ? new ApiErrorItem(ApiErrorCode.Generic, error.Message) // reuse existing message per D-04
        : error;

    var result = new ApiResult();
    result.Errors.Add(wireError);
    return BadRequest(result);
}

private static bool IsAccountEnumerationCode(ApiErrorCode code) =>
    code is ApiErrorCode.InvalidCredentials or ApiErrorCode.UserNotFound;
```
[VERIFIED: IHostEnvironment registered by default in `WebApplication.CreateBuilder` — ASP.NET Core docs]

### Anti-Patterns to Avoid

- **Runtime scrubbing by property name/regex:** Easy to miss new fields. Prefer the allowlist DTO (Pattern 3). D-10 mandates this.
- **Mocking the reCAPTCHA HttpClient via reflection:** Current `_recaptchaHttp` is `static readonly`. Don't swap it at test time — supply a semantically-invalid token instead; Google returns `success=false` which exercises the correct branch.
- **Sharing `WebApplicationFactory` across rate-limit tests:** In-memory fixed-window partition state persists for the factory's lifetime. Per-test factory is non-negotiable.
- **Adding `AttemptStarted` to SiemEventType pre-emptively:** D-11 rejects it. STAB-015's "attempts" category is satisfied by the outcome-on-completion model — every POST emits exactly one SIEM event (success or one of the failure types). Coverage table in §3 below.
- **Setting HSTS `preload`:** D-12 rejects — irreversible once submitted to browser preload lists.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Rate limiter | Custom counter | `Microsoft.AspNetCore.RateLimiting` fixed-window (already wired) | Already shipped; avoids double-counting. |
| RFC 5424 framing | Custom syslog serializer | Extend existing `SiemSyslogFormatter` | Already tested; octet-counting TCP framing already correct (RFC 6587). |
| HTTPS redirect | Custom middleware | `UseHttpsRedirection()` + `UseHsts()` | ASP.NET Core canonical. D-12 locks this. |
| Env-var config source | Custom parser | `AddEnvironmentVariables()` + `__` | Default host builder wires it already. |
| Dev secrets | `.env` files | `dotnet user-secrets` | Official Microsoft tooling; `AddUserSecrets()` active in Development by default when `UserSecretsId` set in csproj. |
| reCAPTCHA mock in tests | HttpClient reflection swap | Invalid-token input | Google's verify endpoint returns `success=false` deterministically; tests remain realistic. |

## Runtime State Inventory

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | None — STAB-013..017 are stateless (wire-response shape, test coverage, log format, redirects, config sources). | None. |
| Live service config | **IIS AppPool env vars** — STAB-017 expects operators to set `SmtpSettings__Password` etc. on the AppPool. Not a migration concern (new deployments only); document the `appcmd` syntax in `docs/IIS-Setup.md`. | Documentation only; installer **does not** write these (D-18). |
| OS-registered state | None — HSTS `max-age` lives in the user agent, but that's a browser concern (already issued by prior deployments). No reset required. | None. |
| Secrets/env vars | SOPS/env var names **unchanged** — STAB-017 introduces **new** env var names (`SmtpSettings__Password`, `PasswordChangeOptions__ServiceAccountPassword`, `ClientSettings__Recaptcha__PrivateKey`); existing deployments continue to read from `appsettings.Production.json` unchanged because env-var precedence is higher but appsettings still works when env vars are absent. | Document the new names; no rename of existing keys. |
| Build artifacts / installed packages | None — no new NuGet packages. `dotnet user-secrets` is built-in. | None. |

## Common Pitfalls

### Pitfall 1: Rate-limiter partition leak across tests
**What goes wrong:** Test A sends 5 requests. Test B's first request hits the 6th counter because they share the factory's in-memory partition.
**Why:** `PartitionedRateLimiter` keyed by IP; `TestServer` uses `127.0.0.1` for all tests.
**How to avoid:** One `WebApplicationFactory` subclass **instance** per test (not per class). Existing `PasswordControllerTests` already does this with `IDisposable` and `_factory = new DebugFactory()` per instance.
**Warning sign:** Flaky 429 in unrelated tests when test order changes.

### Pitfall 2: `IsProduction()` false in integration tests
**What goes wrong:** Planner writes a STAB-013 test expecting `ApiErrorCode.Generic` but `WebApplicationFactory` defaults to `Development` environment — collapse doesn't fire.
**Why:** `WebApplicationFactory` sets `ASPNETCORE_ENVIRONMENT=Development` unless overridden.
**How to avoid:** Test class for STAB-013 must call `builder.UseEnvironment("Production")` (while still disabling Serilog file logging, SIEM, and HTTPS redirect via `AddInMemoryCollection`).
**Warning sign:** Test passes without the STAB-013 gate because Dev leaks the granular code.

### Pitfall 3: SD-PARAM escape omission
**What goes wrong:** Username contains `]` or `"`; syslog parser rejects the record or splits it wrong.
**Why:** RFC 5424 §6.3.3 requires `]`, `"`, `\` to be escaped as `\]`, `\"`, `\\` inside SD-PARAM values.
**How to avoid:** Pure helper `EscapeSdParamValue(string)` with unit tests for each escape.
**Warning sign:** Syslog collector malformed-record alerts.

### Pitfall 4: reCAPTCHA verify endpoint unreachable from CI
**What goes wrong:** Test that relies on Google's verify endpoint fails in offline CI.
**Why:** Google's `siteverify` is a remote dependency; `FailOpenOnUnavailable=false` would then return `InvalidCaptcha` for the wrong reason.
**How to avoid:** Either accept the Google dependency (it's been reliable; `PasswordLogRedactionTests` in repo already hits externals), **OR** add a test-only `IRecaptchaVerifier` abstraction (scope creep beyond D-07). Planner should recommend option 1 with a retry flake guard; if user objects, spike the abstraction in a follow-up plan.
**Warning sign:** Flaky CI on the reCAPTCHA-enabled test.

### Pitfall 5: `ClientSettings.Recaptcha.PrivateKey` env-var binding collision
**What goes wrong:** Operator sets `ClientSettings__Recaptcha__PrivateKey` but the key is `[JsonIgnore]` — no issue, server-side binding is independent of JSON serialization. **This is a false pitfall; documented here to prevent planner confusion.**
**Why:** `[JsonIgnore]` applies only to the outbound `GET /api/password` response. `IOptions<ClientSettings>` binding from `IConfiguration` reads the property regardless.
**How to avoid:** Include a verification test: set `ClientSettings__Recaptcha__PrivateKey` via env var, assert the IOptions value is populated, assert the GET /api/password JSON response does **not** contain the key.

### Pitfall 6: Installer binding check runs before site exists on fresh install
**What goes wrong:** `Get-WebBinding -Name $SiteName -Protocol https` on a fresh install (before `New-Website`) returns null and the check emits a spurious warning.
**Why:** Order-of-operations — the installer creates the site mid-script.
**How to avoid:** Run the binding check **after** `New-Website`/binding configuration, before "install complete" message. The existing installer already has this pattern — place STAB-016 check in the post-binding section (near line 920+).
**Warning sign:** Warning fires on fresh installs when it shouldn't.

## Code Examples

### STAB-013: Production collapse helper
```csharp
// src/PassReset.Web/Controllers/PasswordController.cs
private static bool IsAccountEnumerationCode(ApiErrorCode c) =>
    c is ApiErrorCode.InvalidCredentials or ApiErrorCode.UserNotFound;

private ApiErrorItem RedactIfProduction(ApiErrorItem err) =>
    _hostEnvironment.IsProduction() && IsAccountEnumerationCode(err.ErrorCode)
        ? new ApiErrorItem(ApiErrorCode.Generic, err.Message)
        : err;
```

### STAB-015: SiemService.LogEvent overload
```csharp
// src/PassReset.Web/Services/ISiemService.cs  (new overload)
public interface ISiemService
{
    void LogEvent(SiemEventType eventType, string username, string ipAddress, string? detail = null);
    void LogEvent(AuditEvent evt); // NEW STAB-015
}

// src/PassReset.Web/Services/SiemService.cs  (implementation)
public void LogEvent(AuditEvent evt)
{
    if (_settings.Syslog.Enabled)
        EmitSyslogStructured(evt);
    if (_settings.AlertEmail.Enabled)
        EnqueueAlertEmailFromEvent(evt);
}
```

### STAB-016: Installer binding check
```powershell
# deploy/Install-PassReset.ps1  (new function near line 920)
function Test-HttpsBinding {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SiteName)

    $httpsBindings = @(Get-WebBinding -Name $SiteName -Protocol https -ErrorAction SilentlyContinue)
    if ($httpsBindings.Count -eq 0) {
        Write-Warn "Site '$SiteName' has no HTTPS binding. PassReset requires TLS in production."
        Write-Warn "  - Re-run with -CertThumbprint <thumbprint> to configure HTTPS, or"
        Write-Warn "  - Add an HTTPS binding manually via IIS Manager before exposing the site."
    } else {
        Write-Ok "HTTPS binding verified on site '$SiteName'"
    }
}
# Call after cert binding block (~ line 920)
Test-HttpsBinding -SiteName $SiteName
```

### STAB-017: Env var + user-secrets commands
```powershell
# Operator — AppPool env var (documented in docs/IIS-Setup.md)
& "$env:windir\system32\inetsrv\appcmd.exe" set config `
    -section:applicationPools `
    "/[name='PassReset'].environmentVariables.[name='SmtpSettings__Password',value='<secret>']" `
    /commit:apphost
# Restart the AppPool so the env var is visible to the worker process:
Restart-WebAppPool -Name 'PassReset'
```
```bash
# Developer — user-secrets (documented in CONTRIBUTING.md)
cd src/PassReset.Web
dotnet user-secrets init                                     # one-time; writes UserSecretsId to csproj
dotnet user-secrets set "SmtpSettings:Password" "dev-pass"   # colons OK on CLI; __ convention is env-var only
dotnet user-secrets set "ClientSettings:Recaptcha:PrivateKey" "test-key"
dotnet user-secrets list
dotnet user-secrets remove "SmtpSettings:Password"
# Secrets live on Windows at: %APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json
```
[CITED: learn.microsoft.com/aspnet/core/security/app-secrets, learn.microsoft.com/dotnet/core/tools/dotnet-user-secrets]

## SiemEventType Coverage Audit (STAB-015 D-11)

| STAB-015 category | Existing `SiemEventType` member(s) | Gap? |
|-------------------|-----------------------------------|------|
| Attempts | Every POST produces exactly one outcome event — success or one failure type. There is no "started" event, and STAB-015 text does not require one (it says "cover attempts", which the outcome-on-completion pattern satisfies because every attempt **is** observed). | **No gap. Do not add `AttemptStarted`** per D-11. |
| Failures (AD-layer) | `InvalidCredentials`, `UserNotFound`, `ChangeNotPermitted` | Covered. |
| Failures (portal-layer) | `ValidationFailed`, `RecaptchaFailed`, `PortalLockout`, `ApproachingLockout` | Covered. |
| Rate-limit blocks | `RateLimitExceeded` | Covered. |
| Successes | `PasswordChanged` | Covered. |
| Catch-all / structural errors | `Generic` | Covered. |

**Conclusion:** The 10-member enum is sufficient. STAB-015 adds **structured data fields**, not **new event types**.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Unstructured syslog MSG field | RFC 5424 STRUCTURED-DATA | RFC 5424 superseded RFC 3164 in 2009 | Splunk, QRadar, Graylog all index SD-PARAMs natively. |
| `appsettings.Production.json` cleartext secrets | Env vars → user-secrets → DPAPI (v2.0) | ASP.NET Core 3.0+ | Per-tier: env var = stepping stone (STAB-017); DPAPI/KV = full solution (V2-003). |
| `AddEnvironmentVariables("MYAPP_")` custom prefix | Default `__` convention | ASP.NET Core 3.0 | D-16 chose default — zero code. |
| Runtime scrubbing of sensitive log fields | Compile-time allowlist DTO | Good-practice shift | Prevents miss-by-regex bugs. [ASSUMED: industry trend; no single authoritative citation] |

**Deprecated/outdated:**
- RFC 3164 unstructured syslog — legacy; many tools still accept it but SIEM vendors prefer 5424.
- `.env` files for ASP.NET Core — user-secrets is the official Microsoft path.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `_recaptchaHttp` static HttpClient cannot be mocked without a test-only abstraction; planner will either (a) accept Google verify endpoint dependency in CI or (b) split STAB-014(c) out as needing a small refactor plan | Pitfall 4, Pattern 1 | CI could flake; or STAB-014 needs an extra sub-task. **Ask user which path to take.** |
| A2 | Using `@32473` (IANA reserved private-example PEN) is acceptable for non-public SIEM deployments | Pattern 4 | If user wants PassReset to register its own PEN with IANA, this becomes a separate task. Low risk — internal SIEMs accept any PEN. |
| A3 | `IHostEnvironment.IsProduction()` is the canonical way to gate Production-only behavior; already a documented pattern in `Program.cs` | Pattern 5 | Low — well-established ASP.NET Core API. |
| A4 | Compile-time redaction via `AuditEvent` record is strictly safer than runtime scrubbers; D-10 locks this | Pattern 3 | No risk — locked decision. |
| A5 | `Microsoft.AspNetCore.Mvc.Testing` is already referenced in `PassReset.Tests.csproj`; STAB-014 adds no new packages | Standard Stack | Verify in planning — should be trivial `<PackageReference>` check. |

## Open Questions

1. **reCAPTCHA integration-test strategy for STAB-014(c)**
   - What we know: Current `_recaptchaHttp` is `static readonly` — cannot swap cleanly without refactor. Google's `siteverify` endpoint returns `success=false` for any invalid token, which satisfies the test intent.
   - What's unclear: Whether the user wants the test to hit Google or prefers an abstraction-introducing refactor in this phase.
   - Recommendation: **Hit Google with a syntactically valid but semantically invalid token.** Ship a retry-tolerance guard in the test. If the user requires offline CI, defer STAB-014(c) to a follow-up refactor plan.

2. **IANA Private Enterprise Number for SD-ID**
   - What we know: `@32473` is the IANA reserved example PEN and works technically. Real-world PassReset deployments would want their own PEN.
   - What's unclear: Does the user want PassReset to register a PEN now, or use `@32473` and document that operators can substitute via config?
   - Recommendation: Use `@32473` hardcoded; add follow-up issue to apply for a real PEN. Or make the SD-ID configurable via `SiemSettings.Syslog.StructuredDataId` (default `passreset@32473`).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| `Microsoft.AspNetCore.Mvc.Testing` | STAB-014 | ✓ | 10.0.x (TFM-bound) | — |
| `Microsoft.Extensions.Hosting.Abstractions` (IHostEnvironment) | STAB-013 | ✓ | 10.0.x | — |
| `System.Net.Sockets` (syslog) | STAB-015 (no change) | ✓ | built-in | — |
| `dotnet user-secrets` CLI | STAB-017 (docs only) | ✓ | built-in | — |
| `appcmd.exe` | STAB-017 (operator docs) | ✓ on any IIS-hosting box | built-in (IIS) | PowerShell `Set-WebConfigurationProperty` |
| Google `siteverify` endpoint | STAB-014(c) | ⚠ requires outbound HTTPS from CI | — | Abstraction refactor (Open Q #1) |
| IANA PEN | STAB-015 SD-ID | ⚠ placeholder `@32473` OK | — | Registration deferred (Open Q #2) |

**Missing dependencies with no fallback:** None blocking.

**Missing dependencies with fallback:** Google verify endpoint (Open Q #1).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 (backend), Vitest (frontend — not used this phase) |
| Config file | `src/PassReset.Tests/PassReset.Tests.csproj`, `src/PassReset.Tests/coverage.runsettings` |
| Quick run command | `dotnet test src/PassReset.sln --configuration Release --filter "FullyQualifiedName~Controllers.Password"` |
| Full suite command | `dotnet test src/PassReset.sln --configuration Release` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| STAB-013 | `IsProduction() && InvalidCredentials` → wire returns `Generic=0` | integration | `dotnet test --filter "PasswordControllerRedactionTests"` | ❌ Wave 0 |
| STAB-013 | `IsProduction() && UserNotFound` → wire returns `Generic=0` | integration | same as above | ❌ Wave 0 |
| STAB-013 | `IsProduction()` but non-auth code (e.g. `ChangeNotPermitted`) → wire preserves specific code | integration | same | ❌ Wave 0 |
| STAB-013 | Development environment → wire still returns specific code (regression guard) | integration | same | ❌ Wave 0 |
| STAB-013 | SIEM event granularity preserved (MapErrorCodeToSiemEvent unchanged) | unit | `dotnet test --filter "MapErrorCodeToSiemEvent"` (pattern) | ⚠ may need new unit test class |
| STAB-014(a) | 6th POST in window returns 429 + emits `RateLimitExceeded` | integration | `dotnet test --filter "PasswordControllerRateLimitTests"` | ❌ Wave 0 |
| STAB-014(b) | Non-rate-limited GET path unaffected | integration | same | ❌ Wave 0 |
| STAB-014(c) | reCAPTCHA enabled + invalid token → `InvalidCaptcha` + `RecaptchaFailed` SIEM | integration | `dotnet test --filter "PasswordControllerRecaptchaTests"` | ❌ Wave 0 (see Open Q #1) |
| STAB-014(d) | reCAPTCHA disabled → POST with empty token succeeds | integration | same | ❌ Wave 0 (partially covered by existing `Post_ValidDebugUser_Returns200` which already tests this implicitly) |
| STAB-015 | `AuditEvent` DTO has no Password/Token fields | unit (compile-time + reflection) | `dotnet test --filter "AuditEventShapeTests"` | ❌ Wave 0 |
| STAB-015 | `SiemSyslogFormatter.FormatStructuredData` escapes `]`, `"`, `\` correctly | unit | `dotnet test --filter "SiemSyslogFormatterTests.Structured"` | ⚠ extend existing `SiemSyslogFormatterTests.cs` |
| STAB-015 | `SiemSyslogFormatter` produces RFC-5424-compliant SD element for each event type | unit | same | ⚠ extend existing |
| STAB-015 | SIEM still emits granular `InvalidCredentials` vs `UserNotFound` (coverage audit) | unit | extend existing PasswordController test | ⚠ may need new assertion |
| STAB-016 | `UseHsts()` header present when `EnableHttpsRedirect=true` and environment != Development | integration | `dotnet test --filter "HstsHeaderTests"` | ❌ Wave 0 |
| STAB-016 | Installer `Test-HttpsBinding` warns when no https binding present | manual (or Pester) | `pwsh deploy/tests/Test-Installer.ps1` | ❌ manual-verification (no Pester suite exists today) |
| STAB-017 | Env var `SmtpSettings__Password` overrides appsettings value | integration | `dotnet test --filter "EnvVarConfigurationTests"` | ❌ Wave 0 |
| STAB-017 | `GET /api/password` does not leak `Recaptcha.PrivateKey` even when env-sourced | integration | same (regression against `[JsonIgnore]`) | ❌ Wave 0 |
| STAB-017 | AppPool env var documentation snippet is accurate | manual verification | operator follows docs, confirms worker reads env var | n/a — docs only |

### Sampling Rate
- **Per task commit:** `dotnet test src/PassReset.sln --configuration Release --filter "FullyQualifiedName~Web.Controllers|FullyQualifiedName~Web.Services|FullyQualifiedName~Web.Startup"`
- **Per wave merge:** `dotnet test src/PassReset.sln --configuration Release` (full suite)
- **Phase gate:** Full suite green + manual installer binding-check walkthrough documented in `09-HUMAN-UAT.md` (for STAB-016 installer + STAB-017 AppPool env-var flows — both partly operator-facing)

### Wave 0 Gaps
- [ ] `src/PassReset.Tests/Web/Controllers/PasswordControllerRedactionTests.cs` — covers STAB-013 Production gate (new factory subclass `ProductionEnvFactory` with `builder.UseEnvironment("Production")`)
- [ ] `src/PassReset.Tests/Web/Controllers/PasswordControllerRateLimitTests.cs` — covers STAB-014(a, b)
- [ ] `src/PassReset.Tests/Web/Controllers/PasswordControllerRecaptchaTests.cs` — covers STAB-014(c, d); resolves Open Q #1 first
- [ ] `src/PassReset.Tests/Web/Services/AuditEventShapeTests.cs` — reflection test that asserts `AuditEvent` has no property whose name matches `(?i)password|token|secret|privatekey`
- [ ] `src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs` — **extend** existing file with `FormatStructuredData` escape tests
- [ ] `src/PassReset.Tests/Web/Startup/HstsHeaderTests.cs` — asserts `Strict-Transport-Security` header present in non-Development environments
- [ ] `src/PassReset.Tests/Web/Startup/EnvVarConfigurationTests.cs` — uses `Environment.SetEnvironmentVariable` inside factory setup to prove precedence over appsettings

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | **yes** (STAB-013 enumeration resistance; V2.5.4 "credentials are never disclosed") | Collapse auth failure codes in production responses (STAB-013) |
| V3 Session Management | no — portal is stateless, no cookie session | — |
| V4 Access Control | **partially** — `RestrictedAdGroups` pre-check lives in the provider; no change this phase | Existing group-check remains |
| V5 Input Validation | yes (existing) | `ChangePasswordModel` DataAnnotations + ModelState; rate-limit + reCAPTCHA are V5 adjuncts |
| V6 Cryptography | **yes** (STAB-017 secret storage) | Env vars / user-secrets / (future) DPAPI — never hand-roll |
| V7 Error Handling & Logging | **yes** (STAB-015 structured audit) | RFC 5424 SD + allowlist DTO; V7.1.1 (no sensitive data in logs) satisfied by compile-time redaction |
| V9 Communications | **yes** (STAB-016 HTTPS/HSTS) | `UseHttpsRedirection` + `UseHsts(max-age=1y; includeSubDomains)`; installer binding check |
| V14 Configuration | **yes** (STAB-017) | Secrets externalized from version-controlled config; docs in `Secret-Management.md` |

### Known Threat Patterns for ASP.NET Core + AD password portal

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Account enumeration via distinguishable errors | Information disclosure | Production collapse (STAB-013) + rate-limit (existing) |
| Credential stuffing | Spoofing | reCAPTCHA v3 + rate-limit + AD lockout decorator (existing) + STAB-014 test coverage |
| Secret disclosure via log exfiltration | Information disclosure | Allowlist DTO (STAB-015); `[JsonIgnore]` on `ClientSettings.Recaptcha.PrivateKey` (existing) |
| Cleartext transport | Information disclosure / Tampering | HTTPS + HSTS (STAB-016) |
| Secret disclosure via appsettings.json on disk | Information disclosure | Env-var sourcing (STAB-017) as stepping stone to DPAPI (V2-003) |
| Log injection via syslog MSG | Tampering | RFC 5424 SD-PARAM escape (STAB-015 Pitfall 3) |

## Project Constraints (from CLAUDE.md)

- **Platform:** Windows-only (`net10.0-windows`) — do not recommend cross-platform libraries.
- **Commit convention:** `type(scope): subject` — types `feat|fix|refactor|docs|chore|test|ci|perf|style`; scopes `web|provider|common|deploy|docs|ci|deps|security|installer`. Phase 9 commits will mostly use `feat(security)`, `test(web)`, `docs(security)`, `fix(installer)`.
- **C# style:** File-scoped namespaces; sealed classes where mutation not needed; `_camelCase` private fields; XML docs on public members; `ILogger<T>` injection; error-codes-over-exceptions; **SIEM must not throw on hot path**.
- **Test discipline:** `dotnet test src/PassReset.sln` + `npm test`; coverlet thresholds.
- **Documentation updates:** README, CHANGELOG, relevant `docs/*.md` per release — STAB-017 triggers updates to `docs/IIS-Setup.md`, `docs/Secret-Management.md`, `docs/appsettings-Production.md`, `CONTRIBUTING.md`; STAB-013 triggers `docs/Known-Limitations.md`.
- **GSD workflow:** Work through GSD commands — planner must produce plans before code.

## Sources

### Primary (HIGH confidence)
- Codebase — `src/PassReset.Web/Controllers/PasswordController.cs`, `src/PassReset.Web/Services/SiemService.cs`, `src/PassReset.Web/Program.cs`, `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs`, `deploy/Install-PassReset.ps1`
- RFC 5424 §6.3 "STRUCTURED-DATA"; §7.2.2 "example PEN" — https://datatracker.ietf.org/doc/html/rfc5424
- RFC 6587 octet-counting TCP syslog framing — https://datatracker.ietf.org/doc/html/rfc6587
- Phase 7 CONTEXT (installer helpers), Phase 8 CONTEXT (Options validator pattern) — load-bearing for Phase 9 installer check + future validator hooks

### Secondary (MEDIUM confidence)
- Microsoft Learn — ASP.NET Core Configuration providers and precedence (`learn.microsoft.com/aspnet/core/fundamentals/configuration/`)
- Microsoft Learn — `dotnet user-secrets` (`learn.microsoft.com/aspnet/core/security/app-secrets`)
- Microsoft Learn — AppPool env vars via `appcmd` (`learn.microsoft.com/iis/configuration/system.applicationHost/applicationPools/add/environmentVariables/`)

### Tertiary (LOW confidence / needs validation by planner)
- Compile-time DTO allowlist as an industry-preferred pattern — training-data claim; no single authoritative source. Mitigation: D-10 locks the decision.

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** — all libraries already in the codebase; zero new NuGet/npm; verified against ASP.NET Core 10 defaults.
- Architecture: **HIGH** — STAB-013..017 each land on surfaces already scaffolded (`PasswordController`, `SiemService`, `Install-PassReset.ps1`, `IConfiguration` pipeline); existing `PasswordControllerTests.DebugFactory` is a drop-in pattern for STAB-014.
- Pitfalls: **HIGH** — derived from direct code inspection (rate-limit partition, IHostEnvironment default, static HttpClient reCAPTCHA coupling).
- Validation architecture: **HIGH** — xUnit patterns established; Wave 0 gaps enumerated.
- RFC 5424 SD syntax: **HIGH** — directly cited.

**Research date:** 2026-04-17
**Valid until:** 2026-05-17 (30 days — stable domain; underlying RFC is static; ASP.NET Core 10 is the current LTS target)
