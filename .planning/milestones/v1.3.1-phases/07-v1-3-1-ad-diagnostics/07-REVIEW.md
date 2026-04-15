---
phase: 07-v1-3-1-ad-diagnostics
reviewed: 2026-04-15T00:00:00Z
depth: deep
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
  warning: 2
  info: 4
  total: 6
status: issues_found
---

# Phase 07: Code Review Report (Deep Re-review)

**Reviewed:** 2026-04-15
**Depth:** deep
**Files Reviewed:** 9
**Status:** issues_found

## Summary

Deep cross-file re-review of phase 07 AD diagnostics. The three previously-flagged Warnings (WR-01 template arity, WR-02 sentinel test, WR-03 redundant LogDebug) are confirmed fixed in commits f7d4fbe / 2657152 / f7296c4. Deep cross-file analysis surfaces two Warnings — one upgraded from a prior Info (ExceptionChainLogger lacks cycle/depth bounds; under a malformed or adversarial `InnerException` chain the walker can spin indefinitely and bloat the log event) and one newly identified (the redaction test suite covers a fake provider, not the real `DebugPasswordChangeProvider` nor the real `PasswordChangeProvider` catch blocks that actually call `ExceptionChainLogger`).

Call-chain analysis (Controller.PostAsync → BeginScope(Username/TraceId/ClientIp) → Lockout → PasswordChangeProvider nested BeginScope envelopes → catches → ExceptionChainLogger) is clean: every `BeginScope` uses `using`/`using var`, every `LogContext.PushProperty` in the middleware uses nested `using`, and C# `using` semantics guarantee disposal on all exception paths including mid-flight throws inside nested scopes. Thread safety across the decorator chain is sound — `ConcurrentDictionary.AddOrUpdate` is atomic and Serilog's `LogContext` is `AsyncLocal`, so concurrent requests cannot contaminate each other. No sensitive-data leakage observed on any log emission path except the documented accepted-risk `ExceptionChain` passthrough of AD-supplied `Exception.Message` text (explicitly covered by `ExceptionChainLogger_CapturesInnerExceptionMessages_AcceptedRisk`).

Previously-fixed-Warning verification:
- **WR-01 (template arity)** — confirmed. PasswordChangeProvider lines 505-511 (4 placeholders / 4 args) and 515-517 (2 / 2) and 486-491 (3 / 3) all balanced.
- **WR-02 (sentinel test)** — confirmed. `ExceptionChainLogger_CapturesInnerExceptionMessages_AcceptedRisk` present in PasswordLogRedactionTests.cs:172-192.
- **WR-03 (redundant LogDebug)** — confirmed. LockoutPasswordChangeProvider.IncrementCounter contains only the atomic `AddOrUpdate`; only one `LogWarning("Portal failure counter …")` at line 115 on the call site.

## Warnings

### WR-01: ExceptionChainLogger has no cycle protection or depth bound

**File:** `src/PassReset.PasswordProvider/ExceptionChainLogger.cs:42`
**Issue:** The walker loops `for (var cur = exception; cur is not null; cur = cur.InnerException, depth++)` with no visited-set and no maximum depth. Although `Exception.InnerException` is ordinarily set only via constructor in the BCL, user-constructed or proxied exception graphs (custom exception types that override `InnerException`, certain AggregateException flattening edge cases, or marshalled exceptions from COM interop) can form cycles or arbitrarily deep chains. On a cycle, the loop runs until OOM; on a very deep chain, the resulting `List<object>` bloats the log event and can stall the logging pipeline. At deep scrutiny this is a Warning rather than Info because the walker runs in production catch blocks for exceptions originally thrown by AD / COM interop — code paths outside our control — and a misbehaving AD driver is precisely the scenario these diagnostics were added to debug.
**Fix:**
```csharp
public static void LogExceptionChain(
    ILogger logger,
    Exception exception,
    string messageTemplate,
    params object?[] args)
{
    const int MaxDepth = 16;
    var chain = new List<object>();
    var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
    var depth = 0;
    for (var cur = exception; cur is not null && depth < MaxDepth && seen.Add(cur); cur = cur.InnerException, depth++)
    {
        chain.Add(new
        {
            depth,
            type    = cur.GetType().Name,
            hresult = $"0x{cur.HResult:X8}",
            message = cur.Message,
        });
    }

    using (LogContext.PushProperty("ExceptionChain", chain, destructureObjects: true))
    {
        logger.LogWarning(exception, messageTemplate, args);
    }
}
```

### WR-02: Redaction tests do not exercise the real provider catch paths

**File:** `src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs:46-62,85-95,128-152`
**Issue:** `DebugPasswordChangeProvider_DoesNotLogPlaintext` and `LockoutPasswordChangeProvider_DoesNotLogPlaintext` wire `FakeInvalidCredsProvider` — a minimal two-log-line stub — not the real `DebugPasswordChangeProvider`. The real `PasswordChangeProvider` has multiple production log emission sites that no redaction test covers:

- `ChangePasswordInternal` COMException branches at lines 485, 505, 515 (three separate `ExceptionChainLogger` calls)
- `ValidateUserCredentials` Win32 error LogDebug at line 345
- `ClearMustChangeFlag` catch at line 466
- `ValidateGroups` fallback logging at lines 377 and 386

`PasswordController_DoesNotLogPlaintext_EndToEnd` boots `WebSettings:UseDebugProvider=true`, so end-to-end it also exercises only the debug provider, never the real catch paths that invoke `ExceptionChainLogger`. A future change that accidentally passes `currentPassword` or `newPassword` into one of these templates — the exact threat this test suite is meant to defend against — would pass CI. The test names (`DebugPasswordChangeProvider_*`) are also misleading: they exercise `FakeInvalidCredsProvider`, not `DebugPasswordChangeProvider`.
**Fix:** Add a targeted unit test that invokes `PasswordChangeProvider.ChangePasswordInternal` (or refactor it to be callable without a live `UserPrincipal`) and synthetically throws a `COMException` with `HResult=0x8007202F` to drive the `ExceptionChainLogger.LogExceptionChain(..., comEx.HResult, comEx.Message)` path at line 485. Assert that `CurrentSentinel`/`NewSentinel` never appear in `sink.AllRendered()` or `sink.AllPropertyValues()`. Rename the two fake-based tests to `FakeProvider_DoesNotLogPlaintext` / `LockoutDecorator_DoesNotLogPlaintext_OverFakeInner` to accurately describe their scope.

## Info

### IN-01: ListLogEventSink.Emit is not thread-safe

**File:** `src/PassReset.Tests/Infrastructure/ListLogEventSink.cs:14-16`
**Issue:** `public void Emit(LogEvent logEvent) => Events.Add(logEvent);` uses `List<T>.Add` with no synchronisation. Current tests drive the sink sequentially, so no live bug. If a future test uses `Parallel.For` or drives concurrent `PerformPasswordChangeAsync` calls against `LockoutPasswordChangeProvider` to exercise the ConcurrentDictionary race paths, the sink will corrupt or throw `InvalidOperationException`. Serilog sinks receive events on the caller thread and must be thread-safe if the application logs concurrently.
**Fix:**
```csharp
private readonly object _gate = new();
public List<LogEvent> Events { get; } = new();
public void Emit(LogEvent logEvent) { lock (_gate) Events.Add(logEvent); }
```
Readers (`AllRendered`, `AllPropertyValues`) should also snapshot under the same lock.

### IN-02: TraceIdEnricherMiddleware may be ordered after the request logger

**File:** `src/PassReset.Web/Program.cs:202-206`
**Issue:** Current order is `UseSerilogRequestLogging()` → `UseMiddleware<TraceIdEnricherMiddleware>()`. `UseSerilogRequestLogging` emits its final request-completion line *after* `await next()` returns, at which point the enricher's `using` blocks have already popped `TraceId`/`SpanId` from `LogContext`. Consequence: the per-request summary line (the single most useful correlation point for SIEM) is emitted with `TraceId=unknown`. All log events emitted by inner middleware / MVC handlers are fine — they observe the enriched context while it is still active. Pipeline has no `UseExceptionHandler`, so unhandled exceptions propagate to Kestrel with no additional log emission from this pipeline.
**Fix:** Swap the two registrations:
```csharp
app.UseMiddleware<TraceIdEnricherMiddleware>();
app.UseSerilogRequestLogging();
```
This way the enricher's LogContext is still active when the request-completion line is emitted. Add a regression test that asserts `TraceId` appears as a property on the request-completion LogEvent.

### IN-03: ExceptionChainLogger anonymous-type members are lowercase

**File:** `src/PassReset.PasswordProvider/ExceptionChainLogger.cs:44-50`
**Issue:** The anonymous type uses lowercase member names (`depth`, `type`, `hresult`, `message`). Serilog's destructurer preserves member names verbatim, so the emitted JSON uses lowercase keys while the surrounding property schema is PascalCase (`Username`, `TraceId`, `ClientIp`, `ExceptionChain`). Downstream SIEM JSON-parse rules will need to mix conventions. Not a bug; schema consistency.
**Fix:** Use PascalCase members (`Depth`, `Type`, `HResult`, `Message`) to match the surrounding schema. Update the four assertions in `ExceptionChainLoggerTests.cs:43-46,65-73` accordingly.

### IN-04: PasswordController duplicates TraceId via BeginScope

**File:** `src/PassReset.Web/Controllers/PasswordController.cs:180-186`
**Issue:** The controller reads `Activity.Current?.TraceId.ToString() ?? "unknown"` and pushes it through `BeginScope`, duplicating work already done by `TraceIdEnricherMiddleware`. Serilog de-duplicates by property name, so the middleware's value wins on nested events. Redundant, not incorrect. If the middleware is ever re-ordered (see IN-02) or removed, the two sites could also drift in format. Removing the `TraceId` key from the controller's scope simplifies the mental model and makes the middleware the single source of truth for that property.
**Fix:**
```csharp
using var requestScope = _logger.BeginScope(new Dictionary<string, object>
{
    ["Username"] = model.Username,
    ["ClientIp"] = clientIp,
});
```

---

_Reviewed: 2026-04-15_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
