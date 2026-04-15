---
phase: 07-v1-3-1-ad-diagnostics
reviewed: 2026-04-15T00:00:00Z
depth: standard
files_reviewed: 9
files_reviewed_list:
  - src/PassReset.Web/Middleware/TraceIdEnricherMiddleware.cs
  - src/PassReset.PasswordProvider/ExceptionChainLogger.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs
  - src/PassReset.Tests/Infrastructure/ListLogEventSink.cs
  - src/PassReset.Tests/PasswordProvider/ExceptionChainLoggerTests.cs
  - src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs
findings:
  critical: 0
  warning: 3
  info: 4
  total: 7
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-15
**Depth:** standard
**Files Reviewed:** 9
**Status:** issues_found

## Summary

Phase 07 adds W3C TraceId/SpanId enrichment, structured exception-chain logging, nested `BeginScope` envelopes, and Debug step-enter/exit instrumentation across the AD password-change path, backed by a sentinel-based redaction test suite. The core invariant — passwords never reach logs — is upheld by the code: `currentPassword` and `newPassword` are never passed as log template args, never interpolated into message strings, and never attached to `BeginScope` dictionaries. Scope disposal is handled correctly via `using`/`using var` on every path including exception returns. The `TraceIdEnricher` middleware is ordered correctly relative to `UseSerilogRequestLogging`.

Findings are concentrated in two areas: (1) the redaction-test coverage exercises a local `FakeInvalidCredsProvider` rather than the real `PasswordChangeProvider`, so the COMException-driven `ExceptionChainLogger` call sites (the highest-risk code paths) are not sentinel-asserted end to end; and (2) a message-template / argument-count mismatch in one COMException log statement silently drops two diagnostic values.

## Warnings

### WR-01: Message template arity mismatch in ChangePassword COM error log

**File:** `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:505-507`
**Issue:** The template has two named holes (`{HResult}`, `{Status}`), but four positional args are passed. Serilog binds the first two and silently attaches the extra two as positional `__1`/`__2` properties, so the intended `UseAutomaticContext` and `AllowSetPasswordFallback` diagnostic values are effectively invisible in rendered output and ambiguously named in structured sinks — defeating the purpose of the extra telemetry added in this phase.
**Fix:**
```csharp
ExceptionChainLogger.LogExceptionChain(_logger, comEx,
    "ChangePassword failed (HRESULT={HResult}); SetPassword fallback is {Status} " +
    "(UseAutomaticContext={UseAutomaticContext}, AllowSetPasswordFallback={AllowSetPasswordFallback})",
    comEx.HResult,
    _options.AllowSetPasswordFallback ? "disabled (auto context)" : "disabled",
    _options.UseAutomaticContext,
    _options.AllowSetPasswordFallback);
```

### WR-02: Redaction tests do not exercise the real PasswordChangeProvider log paths

**File:** `src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs:46-62, 129-152`
**Issue:** The sentinel assertions run against `FakeInvalidCredsProvider` (a local stub that logs only `{User}` templates) and an end-to-end test that forces `UseDebugProvider=true`. Neither exercises `PasswordChangeProvider.PerformPasswordChangeAsync` — and therefore never touches the `DirectoryServicesCOMException`, `PasswordException`, or `PrincipalOperationException` catch blocks where `ExceptionChainLogger` emits `cur.Message` / `comEx.Message` into structured properties. These are the highest-risk paths for accidental password disclosure if AD ever surfaces a password in an error message (historically rare, but BUG-002's fallback path logs `comEx.Message` directly). The test suite's positive-control assertion (`Debug or Information` event present) therefore passes without proving the real provider is redaction-safe.
**Fix:** Add a test that invokes `PasswordChangeProvider` directly with a mocked/wrapped `PrincipalContext` (or at minimum, unit-test `ExceptionChainLogger` with a `PasswordException` whose `.Message` contains the `NewSentinel` — assert the sentinel does reach the structured property and document that AD-message passthrough is an accepted risk). Alternatively, invoke `ExceptionChainLogger.LogExceptionChain` directly in a redaction test with a synthesised chain whose inner-most `Exception.Message` contains both sentinels, then assert the sentinels DO appear (proving the chain walker faithfully captures AD messages) — and add an explicit note in the test that AD-supplied exception messages are the only permitted leakage channel.

### WR-03: Redundant Debug + Warning log for every lockout failure increment

**File:** `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs:115-121`
**Issue:** Every `InvalidCredentials` response now emits both `LogDebug("lockout counter {Count}/{Threshold} for {Username}")` and `LogWarning("Portal failure counter for {Username}: {Count}/{Threshold}")` carrying the same three properties. This doubles the per-failure log volume on the warning channel without adding information and pollutes SIEM output. The Debug-level step-enter/exit pattern used elsewhere in this phase is fine; the duplicate Warning is likely a merge artefact.
**Fix:** Remove the `LogDebug` call (or downgrade the `LogWarning` to `LogInformation` if the warning level is the redundant one — pick whichever the phase plan intended). Keep exactly one emission per counter increment.

## Info

### IN-01: TraceId captured twice in PasswordController request scope

**File:** `src/PassReset.Web/Controllers/PasswordController.cs:180-186`
**Issue:** `TraceIdEnricherMiddleware` already pushes `TraceId` onto `LogContext` for the whole request. Re-reading `Activity.Current?.TraceId` into the controller's `BeginScope` dictionary is redundant and risks divergence if the two sources ever disagree (e.g., after a middleware replaces the current Activity). Downstream log events end up with two `TraceId` properties (middleware-pushed and scope-pushed) — sink behaviour on duplicate keys is sink-specific.
**Fix:** Drop `["TraceId"] = traceId` from the controller scope dictionary; rely on the middleware-pushed value. Keep `Username` and `ClientIp` — those are scope-local.

### IN-02: `ActiveEntries` LINQ Count enumerates a concurrent dictionary without snapshot

**File:** `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs:80`
**Issue:** `_counters.Count(kvp => DateTimeOffset.UtcNow < kvp.Value.Expiry)` enumerates `ConcurrentDictionary` which is safe but returns a "best-effort" snapshot that can double-count or skip entries concurrently mutated. Acceptable for a diagnostics counter, but worth a comment so future maintainers don't rely on exactness.
**Fix:** Add a `// Best-effort snapshot — may over/undercount under contention; diagnostics only.` comment above the getter, or cache the value in an `Interlocked`-tracked field updated inside `IncrementCounter`/`EvictExpiredEntries`.

### IN-03: `EvictExpiredEntries` safety-cap reads `_counters.Count` three times

**File:** `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs:206-214`
**Issue:** The `> MaxEntries` check, the `Count / 4` calculation inside `Take(...)`, and the warning-log argument each read `_counters.Count` independently. Between reads the dictionary can shrink (e.g., due to `TryRemove` in another thread), so the eviction count can be smaller than logged. Low-impact because this is a safety valve, not a correctness path.
**Fix:** Snapshot once: `var count = _counters.Count; if (count > MaxEntries) { var quarter = count / 4; ... Take(quarter) ... }` and log `count`, `quarter`.

### IN-04: Username embedded in `InvalidOperationException` message

**File:** `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:126-127`
**Issue:** `throw new InvalidOperationException($"User has neither UserPrincipalName nor SamAccountName (input: {username})")` — the throw is caught by the generic `catch (Exception ex)` at line 187 and logged via `_logger.LogError(ex, ...)`, which renders `ex.Message` including the raw input. Username is not a password, but the rest of the phase deliberately routes usernames only through structured scope properties (redactable) rather than interpolated messages. This one break in discipline could make username-redaction rules harder to implement later.
**Fix:** Throw with a templated message that omits the input, and log the input separately via the existing Username scope:
```csharp
throw new InvalidOperationException("User has neither UserPrincipalName nor SamAccountName");
```
The surrounding `BeginScope` on line 181 of `PasswordController.cs` already carries `Username` into the generic-catch log event.

---

_Reviewed: 2026-04-15_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
