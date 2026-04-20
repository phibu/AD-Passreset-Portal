---
phase: 10-operational-readiness
plan: 01
subsystem: web
tags: [health, diagnostics, observability, stab-018]
requirements_addressed: [STAB-018]
dependency_graph:
  requires:
    - src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs (ILockoutDiagnostics analog pattern)
  provides:
    - src/PassReset.Web/Services/IExpiryServiceDiagnostics.cs
    - src/PassReset.Web/Services/NullExpiryServiceDiagnostics.cs
    - GET /api/health (enriched nested-checks contract, D-01)
  affects:
    - src/PassReset.Web/Services/PasswordExpiryNotificationService.cs
    - src/PassReset.Web/Controllers/HealthController.cs
    - src/PassReset.Web/Program.cs (DI wiring for all three branches)
tech_stack:
  added: []
  patterns:
    - Null-object pattern for disabled diagnostics (NullExpiryServiceDiagnostics)
    - Async socket probe with CancellationTokenSource deadline (3s)
    - Aggregate rollup with neutral states (skipped, not-enabled)
    - Atomic LastTick via Interlocked.Exchange on a long field
key_files:
  created:
    - src/PassReset.Web/Services/IExpiryServiceDiagnostics.cs
    - src/PassReset.Web/Services/NullExpiryServiceDiagnostics.cs
    - src/PassReset.Tests/Services/ExpiryServiceDiagnosticsTests.cs
    - src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs
    - src/PassReset.Tests/Services/SmtpProbeTests.cs
  modified:
    - src/PassReset.Web/Controllers/HealthController.cs
    - src/PassReset.Web/Services/PasswordExpiryNotificationService.cs
    - src/PassReset.Web/Program.cs
decisions:
  - Null-object over nullable IExpiryServiceDiagnostics — keeps HealthController injection non-nullable and simplifies the three DI branches (debug / prod-disabled / prod-enabled).
  - Aggregate treats skipped + not-enabled as neutral — avoids false 503s on deployments that intentionally disable email or expiry notifications.
  - TCP listener-backed fake AD endpoint in test factories — CI has no domain controller; a loopback TcpListener satisfies both the options validator (LdapHostnames non-empty when UseAutomaticContext=false) and the health probe (reachable → healthy) without mocking `System.DirectoryServices`.
metrics:
  duration_minutes: ~35
  completed: 2026-04-20
  tasks_completed: 2
  tests_added: 11
  files_created: 5
  files_modified: 3
---

# Phase 10 Plan 01: Enrich /api/health with AD + SMTP + ExpiryService checks — Summary

One-liner: Replaced the single-bit AD `/api/health` probe with a nested D-01 readiness contract (AD + SMTP + ExpiryService) using `TcpClient.ConnectAsync` with a 3 s `CancellationTokenSource` deadline, atomic expiry-service diagnostics, and an aggregate rollup that leaks no secrets.

## Tasks executed

### Task 1 — IExpiryServiceDiagnostics + atomic LastTick (commit `45452b2`)

- Added `IExpiryServiceDiagnostics` interface (`IsEnabled`, `LastTickUtc`) mirroring the shape of `ILockoutDiagnostics`.
- Extended `PasswordExpiryNotificationService` to implement the interface with an atomic `long _lastTickTicks` updated via `Interlocked.Exchange` after every successful run of `RunNotificationsAsync`.
- Added `ExpiryServiceDiagnosticsTests` covering `IsEnabled` propagation, null-before-first-tick, post-tick value, and concurrent-reader torn-read safety (100 readers × 1 writer via `Parallel.For`, no exceptions).

### Task 2 — HealthController nested checks + DI wiring + tests (commit `2891ba0`)

- Rewrote `HealthController.Get()` as `async Task<IActionResult> GetAsync()` returning the D-01 nested shape:

  ```json
  {
    "status": "healthy|degraded|unhealthy",
    "timestamp": "...",
    "checks": {
      "ad":            { "status", "latency_ms", "last_checked" },
      "smtp":          { "status", "latency_ms", "last_checked", "skipped" },
      "expiryService": { "status", "latency_ms", "last_checked" }
    }
  }
  ```

- Aggregate rollup: `unhealthy > degraded > healthy`; `skipped` and `not-enabled` are neutral so a deployment that disables email + expiry reports overall `healthy`.
- SMTP probe: `CheckSmtpAsync` short-circuits to `("skipped", 0, true)` when both `EmailNotificationSettings.Enabled` and `PasswordExpiryNotificationSettings.Enabled` are false; otherwise runs `TcpClient.ConnectAsync(host, port, cts.Token)` under a 3 s CTS.
- Refactored `CheckAdConnectivity` to async `CheckAdConnectivityAsync`; replaced every sync `TcpClient.Connect(...)` with the `ConnectAsync + 3s CTS` pattern (no sync connect remains in the controller).
- `Program.cs` DI: added `NullExpiryServiceDiagnostics` (internal sealed) wired into the debug branch and the prod-disabled branch; in the prod-enabled branch registers `PasswordExpiryNotificationService` as singleton + `AddHostedService(sp => sp.GetRequiredService<...>())` + `AddSingleton<IExpiryServiceDiagnostics>(sp => sp.GetRequiredService<PasswordExpiryNotificationService>())` so the hosted lifecycle and the health controller share the same instance.
- Added `HealthControllerTests` (6 integration tests) and `SmtpProbeTests` (1 unit test asserting `ConnectAsync` honours the 3 s CTS against a TEST-NET-1 / RFC 5737 blackhole).
- Added `FakeAdFactory` base class with a 127.0.0.1:0 `TcpListener` that accepts + closes connections — used to satisfy both the options validator and the AD probe in CI without a real domain controller.

## Verification

- Targeted: `dotnet test --filter "FullyQualifiedName~HealthControllerTests|FullyQualifiedName~SmtpProbeTests"` → 7/7 green (Task 2 alone).
- Full suite: `dotnet test src/PassReset.sln --configuration Release` → **179/179 passed**, 0 failures, 0 skipped (Duration 3 s).
- Build: `dotnet build src/PassReset.sln --configuration Release` → 0 errors (warnings are pre-existing xUnit1051 analyzer notes on `CancellationToken` usage in the rest of the test suite).

## Acceptance criteria — all met

- `grep "public async Task<IActionResult> GetAsync" HealthController.cs` — present.
- `grep "ConnectAsync" HealthController.cs` — matches for both AD and SMTP probes; no `TcpClient.Connect(` (sync) remaining.
- `grep "skipped" HealthController.cs` — shows the `("skipped", 0, true)` short-circuit branch.
- DI wiring in `Program.cs` — `NullExpiryServiceDiagnostics` registered in debug branch + prod-disabled branch; `PasswordExpiryNotificationService` singleton + hosted + diagnostics cast in prod-enabled branch. All three branches inject a non-null `IExpiryServiceDiagnostics`.
- HealthControllerTests: 6 tests green (shape, smtp-skipped, expiry-not-enabled-healthy, no-secrets, all-healthy-200, any-unhealthy-503).
- SmtpProbeTests: 1 test green, elapsed < 4 s against blackhole target.
- ExpiryServiceDiagnosticsTests: 4 tests green from Task 1 (unchanged, still passing).

## Deviations from Plan

- **[Rule 3 — Blocking issue] CI-friendly fake AD listener.**
  - **Found during:** Task 2 test execution.
  - **Issue:** The original plan assumed `UseAutomaticContext=true` or empty `LdapHostnames` would yield a healthy AD probe in CI. Reality: `UseAutomaticContext=true` throws `PrincipalContext` errors without a domain; `UseAutomaticContext=false` + empty `LdapHostnames` is blocked by `PasswordChangeOptionsValidator` with "must contain at least one non-empty hostname when UseAutomaticContext is false".
  - **Fix:** Introduced a `FakeAdFactory` base class for test fixtures that binds a `TcpListener` to `127.0.0.1:0` and accepts + closes connections in a background loop. Fixtures seed `PasswordChangeOptions.LdapHostnames=["127.0.0.1"]` + `LdapPort=<bound port>`, satisfying the validator AND the health probe without mocking `System.DirectoryServices`.
  - **Files modified:** `src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs`.
  - **Commit:** `2891ba0`.

No other deviations — plan executed as written.

## Threat Flags

None introduced. T-10-01-01 (Information Disclosure — secrets in body) is mitigated by `Health_Body_ContainsNoSecrets` asserting the absence of seeded SMTP password and reCAPTCHA private-key sentinels plus a defensive `"password"` / `"privateKey"` substring check. T-10-01-02 (DoS via SMTP probe) is mitigated by `SmtpProbeTests.ConnectAsync_RespectsCancellationToken` proving the 3 s CTS fires within tolerance.

## Known Stubs

None.

## Self-Check: PASSED

- src/PassReset.Web/Controllers/HealthController.cs — FOUND (modified).
- src/PassReset.Web/Program.cs — FOUND (modified).
- src/PassReset.Web/Services/NullExpiryServiceDiagnostics.cs — FOUND (new).
- src/PassReset.Web/Services/IExpiryServiceDiagnostics.cs — FOUND (Task 1).
- src/PassReset.Tests/Web/Controllers/HealthControllerTests.cs — FOUND (new).
- src/PassReset.Tests/Services/SmtpProbeTests.cs — FOUND (new).
- src/PassReset.Tests/Services/ExpiryServiceDiagnosticsTests.cs — FOUND (Task 1).
- Commit `45452b2` (Task 1) — FOUND in `git log`.
- Commit `2891ba0` (Task 2) — FOUND in `git log`.
