---
phase: 07-v1-3-1-ad-diagnostics
verified: 2026-04-15T00:00:00Z
status: passed
score: 7/7 success criteria verified
overrides_applied: 0
---

# Phase 07 — AD Diagnostics (BUG-004) Verification Report

**Phase Goal:** Diagnose intermittent `0x80070005 (E_ACCESSDENIED)` AD failures via structured logging; external behavior unchanged.
**Verified:** 2026-04-15
**Status:** PASS
**Score:** 7/7 ROADMAP success criteria verified

## Per-Criterion Evidence

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | Step events for user-lookup / ChangePasswordInternal / Save with AD-context scope once per request | ✓ PASS | `PasswordChangeProvider.cs:45-55` (user-lookup), `:136-146` (change-password-internal), `:151-161` (save); AD-context scope at `:67-73` opened only inside `if (userPrincipal != null)` branch with Domain, DomainController (`principalContext.ConnectedServer ?? "unknown"`), IdentityType, UserCannotChangePassword, LastPasswordSetUtc (ISO-8601). |
| 2 | ExceptionChain array with {depth, type, hresult, message} for COMException + PasswordException | ✓ PASS | `ExceptionChainLogger.cs:41-53` walks `InnerException`, emits `{depth, type, hresult=0x{HResult:X8}, message}` via `LogContext.PushProperty("ExceptionChain", chain, destructureObjects:true)`. Invoked at `PasswordChangeProvider.cs:174` (PasswordException), `:195` (DirectoryServicesCOMException), and inner `ChangePasswordInternal` COMException paths at `:485/505/511`. |
| 3 | W3C TraceId correlation on every entry (not HttpContext.TraceIdentifier) | ✓ PASS | `TraceIdEnricherMiddleware.cs:38-42` pushes `Activity.Current?.TraceId.ToString()` + `SpanId`; registered in `Program.cs:206`. Controller outer scope (`PasswordController.cs:180-185`) also propagates TraceId. |
| 4 | Targeted catches, PrincipalOperationException distinct | ✓ PASS | `PasswordChangeProvider.cs:171` PasswordException → LogExceptionChain; `:178` PrincipalOperationException → plain `LogWarning(ex, ...)` default destructure; `:193` generic Exception with COMException branch routed through LogExceptionChain. |
| 5 | Lockout Debug events + preserved Warnings | ✓ PASS | `LockoutPasswordChangeProvider.cs:115-117` counter-increment Debug, `:195` sweep-start Debug, `:225` evicted Debug; Warning logs at `:96-100` (PortalLockout) and `:119-123` (Approaching) preserved unchanged. |
| 6 | No plaintext leaks — PasswordLogRedactionTests | ✓ PASS | `PasswordLogRedactionTests.cs` 3 Facts (DebugProvider / Lockout / E2E via WebApplicationFactory) assert sentinels absent from `AllRendered()` + `AllPropertyValues()` with positive-control event-count check. `ExceptionChainLoggerTests.cs` verifies chain shape (single, nested depth 0/1 with 0x80070005, top-level exception ref equality). |
| 7 | User-facing response shape byte-identical to v1.3.0 | ✓ PASS | `PasswordController.PostAsync` return paths unchanged: `BadRequest(ApiResult.FromModelStateErrors)` `:151`, `BadRequest(result)` `:163/197`, `BadRequest(ApiResult.InvalidCaptcha())` `:173`, `Ok(new ApiResult("Password changed successfully."))` `:225`. No ApiErrorCode changes. No new config keys (grep against phase dir shows only pre-existing appsettings references). Existing PasswordControllerTests pass unchanged. |

## Build & Test Results

- `dotnet build src/PassReset.sln -c Release` → **0 errors**, 10 pre-existing xUnit1051 warnings (unrelated, CancellationToken advisories)
- `dotnet test src/PassReset.sln -c Release` → **80/80 passed**, 0 failed, 0 skipped
- `npm run build` (ClientApp) → **green** (built in 177ms; bundle size unchanged vs baseline)

## Overall Verdict: PASS

All 7 ROADMAP success criteria delivered by committed code. Planned decisions D-01..D-05 honored: W3C TraceId (not TraceIdentifier), ExceptionChainLogger scoped to COM/Password only, PrincipalOperationException distinct catch with default destructure, nested scopes with Stopwatch ElapsedMs, AD-context scope gated on non-null principal. No new config keys; user-facing API unchanged.

_Verifier: Claude (gsd-verifier)_
