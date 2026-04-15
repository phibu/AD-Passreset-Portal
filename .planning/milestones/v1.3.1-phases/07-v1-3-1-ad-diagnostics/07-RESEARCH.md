# Phase 07: v1.3.1 AD Diagnostics - Research

**Researched:** 2026-04-15
**Domain:** Structured logging / diagnostic refactor (Serilog + ASP.NET Core 10 + System.DirectoryServices)
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
1. **Correlation ID = W3C `Activity.TraceId`** via small middleware that calls `LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString())`. Do NOT emit `HttpContext.TraceIdentifier`.
2. **Step-granular logging = `ILogger.BeginScope` envelopes** — `PasswordController.PostAsync` opens an outer scope (`Username`, `TraceId`, `ClientIp`); provider methods open nested scopes. Step before/after emitted as single-line **Debug** events (e.g. `"user-lookup: start"` / `"user-lookup: complete duration={ElapsedMs}"`).
3. **Exception-chain walker** for `DirectoryServicesCOMException` and `PasswordException` ONLY — helper `LogExceptionChain(ILogger, Exception)` emits structured `ExceptionChain` array `[{depth, type, hresult, message}...]`. Other exceptions keep existing `LogWarning(ex,…)` / `LogError(ex,…)` destructure.
4. **AD context scope** — after user principal resolved, open a scope with `Domain`, `DomainController` (from `PrincipalContext.ConnectedServer`), `IdentityType`, `UserCannotChangePassword`, `LastPasswordSetUtc` (ISO 8601 or null). Inherited by downstream logs.
5. **Redaction safety net** = xUnit test (`PasswordLogRedactionTests`) using sentinel plaintext (`"SENTINEL_CURRENT_12345"`, `"SENTINEL_NEW_67890"`) asserted absent from rendered messages + property bags. No runtime filter.

### Claude's Discretion
- Test sink choice (see §Test Strategy below — recommended: handwritten `ILogEventSink` appending `LogEvent` to `List<LogEvent>`).
- File name for new middleware and exception helper (see §Files Likely Touched in CONTEXT.md).

### Deferred Ideas (OUT OF SCOPE)
- OpenTelemetry exporters · aggregation services (Seq/Elastic) · metrics/counters · sink changes · file path/retention changes · user-facing response changes.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| BUG-004 | Structured diagnostic logging around every step of AD password-change flow; correlate via TraceId; full exception chain with HResult/depth; targeted catches for `PasswordException`/`PrincipalOperationException`/`DirectoryServicesCOMException`; AD context captured; lockout transitions logged; no plaintext leaks; no response changes. | All sections below — codebase inventory (§Current State), API verification (§Library / API Verification), test strategy (§Test Strategy). |
</phase_requirements>

## Summary

This is a **diagnostic refactor** of an existing, well-structured codebase — not greenfield. Serilog is already wired (`UseSerilog` + `FromLogContext` in `Program.cs:26–29`), `ILogger<T>` is injected in every class that matters, and there is precedent for rich exception logging (see `PasswordChangeProvider.cs:411–441` for the existing `DirectoryServicesCOMException` handling with HResult formatting). The work is to:

1. Insert a ~15-line middleware that pushes `Activity.Current.TraceId` onto `LogContext` per request.
2. Replace flat `LogInformation` calls at flow boundaries with `BeginScope` envelopes that name each step (`user-lookup`, `credential-validate`, `change-password-internal`, `save`).
3. Extract a new `ExceptionChainLogger` helper that walks `InnerException` for the two rich exception types.
4. Add AD-context scope after `FindUser` returns a principal.
5. Add eviction-count Debug event in the lockout decorator.
6. Add two new test files (redaction + chain-walker unit tests).

**Primary recommendation:** Prefer a handwritten `ListLogEventSink` (< 30 lines) over adding `Serilog.Sinks.XUnit` or `Serilog.Sinks.TestCorrelator` as new dependencies — the existing test project uses `NSubstitute` + xUnit v3 with zero Serilog test packages, and adding one risks pulling old `Serilog.Core` versions into the test graph. See §Test Strategy.

## Current State (Codebase Inventory)

### Existing catch blocks (where new structured logging slots in)

| File:Line | Exception | Current behavior | Change |
|-----------|-----------|------------------|--------|
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:115–119` | `PasswordException` | `LogWarning(passwordEx, "Password complexity error for user {Username}", username)` | Route through `LogExceptionChain` before returning the `ApiErrorItem` |
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:121–129` | `Exception` (generic) | `LogError(ex, "Unexpected error...")` | Keep as-is for non-targeted types; if `ex is DirectoryServicesCOMException`, route through `LogExceptionChain` |
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:402–442` | `COMException` inside `ChangePasswordInternal` | Classifies E_ACCESSDENIED (0x80070005) and ERROR_DS_CONSTRAINT_VIOLATION (0x8007202F); logs with HResult | Wrap existing `LogWarning(comEx,…)` calls in a `LogExceptionChain` call — current code only shows top-level HResult, no inner frames |
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:301–322` | `Exception` around `GetGroups()` / `GetAuthorizationGroups()` | Logs fallback | Keep — not in scope for BUG-004's exception-chain walker |
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:151–154, 203–206, 249–253, 389–393` | `Exception` (generic) | `LogError`/`LogWarning` | Keep as-is — default destructure is fine |
| `src/PassReset.Web/Controllers/PasswordController.cs:271–296` | reCAPTCHA (`HttpRequestException`, `TaskCanceledException`, generic) | `LogError`/`LogWarning` | Out of scope — not AD path |

**Note:** `PrincipalOperationException` is mentioned in ROADMAP success criteria #3 but CONTEXT.md §3 limits the chain walker to `DirectoryServicesCOMException` and `PasswordException`. The plan should add a `catch (PrincipalOperationException)` in `PerformPasswordChangeAsync` **between** the `PasswordException` catch (line 115) and the generic `Exception` catch (line 121), logging HResult + chain via the default destructure path. The CONTEXT decision means we do *not* emit a structured `ExceptionChain` array for it — only a targeted catch with distinct context.

### Logging call sites to convert to Debug step events

Lines in `PasswordChangeProvider.cs` currently at `Information`/`Warning` that represent **step entry/exit** (not outcomes) should drop to `Debug`:
- `:75` — `"PerformPasswordChange for user {Username}"` → becomes scope property, drop the log line (or demote to Debug `"flow: start"`)
- `:525` — `LogDebug("Acquiring domain context via AutomaticContext")` — already Debug, keep
- `:530` — `LogDebug("Acquiring domain context for {Server}...")` — already Debug, keep
- `:271` — `LogDebug("ValidateUserCredentials Win32 error code: {ErrorCode}")` — already Debug, keep

**Outcomes stay at Information/Warning** (unchanged semantics):
- `:113` `"Password changed successfully for user {Username}"` — Information
- `:43, 66, 70, 83, 102` — Warning (user-not-found / fail-open / restricted / invalid-creds)

### Program.cs middleware ordering (Program.cs:198–255)

Current pipeline order:
```
1. UseSerilogRequestLogging()        // line 201  -- must stay FIRST after Build
2. custom security-headers lambda    // line 204
3. UseHttpsRedirection (conditional) // line 230
4. UseDefaultFiles / UseStaticFiles  // line 233-248
5. UseRouting()                      // line 250
6. UseRateLimiter()                  // line 253
7. MapControllers() / MapFallback    // line 255-258
```

**Insertion point for `TraceIdEnricherMiddleware`:** Place **after** `UseSerilogRequestLogging()` (line 201) and **before** `UseRouting()` (line 250). The middleware must run before any logger call that needs the TraceId property — so earliest practical is immediately after the Serilog request-logging middleware. ASP.NET Core auto-creates the hosting `Activity` well before user middleware (in `HostingApplication`), so `Activity.Current` is non-null by the time any user middleware executes — **VERIFIED** via `dotnet/aspnetcore` source (Microsoft.AspNetCore.Hosting.HostingApplicationDiagnostics).

### Test infrastructure (verified, src/PassReset.Tests)

- xUnit **v3 3.2.2** + xunit.runner.visualstudio 3.* (`PassReset.Tests.csproj:24–25`)
- NSubstitute used for `ILogger<T>` stubs (`LockoutPasswordChangeProviderTests.cs:28`: `Substitute.For<ILogger<LockoutPasswordChangeProvider>>()`)
- `WebApplicationFactory<Program>` already in use (`PasswordControllerTests.cs:17–29`) with a private `DebugFactory` subclass. `Program` is exposed via `public partial class Program { }` at `Program.cs:275`.
- NO Serilog test packages currently referenced. Zero test-project dependency on `Serilog.*`.
- xUnit v3 `Assert.DoesNotContain` + string.Contains is adequate for the plaintext sentinel assertion.

### Lockout decorator — current state

- Counter increment at `LockoutPasswordChangeProvider.cs:115–117` — currently `LogWarning` (keep; already structured).
- `ApproachingLockout` / `PortalLockout` — existing `LogWarning` at `:96–98` (keep).
- `EvictExpiredEntries` — `:218–219` currently `LogDebug("Evicted {Count} lockout entries")` — **already exists**, CONTEXT.md wording "new `Debug` event" is slightly stale. The plan only needs to ensure it emits **even when `evicted == 0` is false** (already does). Optional: add `LogDebug("Evicting lockout entries: active={Active}, total={Total}")` at start of sweep to capture silent-no-op sweeps too.
- Dictionary overflow warning at `:202–204` — keep.

## Library / API Verification

### 1. `Activity.TraceId` — W3C format

```csharp
// Namespace: System.Diagnostics
// Activity.Current is null-safe to read anywhere after hosting middleware populates it.
var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString(); // 32-char lowercase hex
var spanId  = System.Diagnostics.Activity.Current?.SpanId.ToString();  // 16-char lowercase hex
```

- `ActivityTraceId.ToString()` is documented to return the hex representation (no dashes). **VERIFIED** from `dotnet/runtime` source — `ActivityTraceId.ToHexString()` is what `.ToString()` delegates to.
- No package reference needed — `System.Diagnostics.DiagnosticSource` is transitively referenced by ASP.NET Core.

### 2. `Serilog.Context.LogContext.PushProperty` — idiomatic enrichment

```csharp
// Namespace: Serilog.Context (from Serilog.AspNetCore 10.0.0 — already referenced)
using Serilog.Context;

public async Task Invoke(HttpContext context, RequestDelegate next)
{
    var traceId = Activity.Current?.TraceId.ToString() ?? "unknown";
    using (LogContext.PushProperty("TraceId", traceId))
    {
        await next(context);
    } // property scope ends when IDisposable is disposed
}
```

- `FromLogContext` is already wired at `Program.cs:29` — pushed properties automatically flow into every `LogEvent` emitted during the scope. **VERIFIED** via Serilog docs (Enrichment > FromLogContext).
- `PushProperty` returns `IDisposable` — critical that `using` brackets the call to `next`, otherwise the property leaks to the thread pool.

### 3. `ILogger.BeginScope` + Serilog

Serilog's `SerilogLoggerProvider` translates `BeginScope(new { Key = value, … })` into `LogEvent` properties. Two supported shapes:

```csharp
// Anonymous object — each property becomes a separate LogEvent property.
using (_logger.BeginScope(new { Username = username, Step = "user-lookup" }))
{
    _logger.LogDebug("user-lookup: start");
}

// Dictionary — also supported.
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["Domain"] = domain,
    ["DomainController"] = dc,
    ["IdentityType"] = idType.ToString(),
    ["UserCannotChangePassword"] = ucp,
    ["LastPasswordSetUtc"] = lastSetUtc?.ToString("o") ?? "null",  // ISO 8601 round-trip
}))
{
    // …every log inside this block gets all 5 properties.
}
```

- The anonymous-object form is preferred for 1–3 properties; the dictionary form is preferred for 4+ (matches existing codebase style — there is currently no `BeginScope` usage so no style to conflict with).
- Properties appear in `{Properties:j}` output and in file-sink JSON. **VERIFIED** via Serilog.Extensions.Logging source (SerilogLogger.BeginScope).

### 4. `PrincipalContext.ConnectedServer`

```csharp
// Microsoft docs: System.DirectoryServices.AccountManagement.PrincipalContext.ConnectedServer
// Type: string
// Returns the name of the domain controller that the PrincipalContext is connected to.
// Populated lazily on first bind — safe to read AFTER FindUser() has returned non-null.
var dc = principalContext.ConnectedServer; // e.g. "DC01.contoso.local"
```

- **VERIFIED** via Microsoft Learn docs (PrincipalContext.ConnectedServer Property). Null-safe: if the context has not yet bound, returns null.
- Read it **after** `FindUser(ctx, username)` returns non-null — a bind has definitely occurred.

### 5. `UserPrincipal.LastPasswordSet`

```csharp
// Type: DateTime? (nullable)
// null = the pwdLastSet attribute is 0 (must-change-at-next-logon) or unreadable
// otherwise: local-kind DateTime; convert to UTC before emitting to logs.
DateTime? lastSetUtc = userPrincipal.LastPasswordSet?.ToUniversalTime();
var iso = lastSetUtc?.ToString("o"); // ISO 8601 round-trip "2026-04-15T12:34:56.0000000Z"
```

- **VERIFIED** — property type is `DateTime?` per Microsoft Learn. The codebase already handles this correctly at `PasswordChangeProvider.cs:352–356`.
- Use format specifier `"o"` (round-trip) — produces unambiguous ISO 8601 with Z suffix when the DateTime Kind is UTC.

### 6. `DirectoryServicesCOMException` — exception chain walk

```csharp
public static void LogExceptionChain(ILogger logger, Exception ex, string messageTemplate, params object?[] args)
{
    var chain = new List<object>();
    var depth = 0;
    for (var cur = ex; cur is not null; cur = cur.InnerException, depth++)
    {
        chain.Add(new
        {
            depth,
            type    = cur.GetType().Name,
            hresult = cur is System.Runtime.InteropServices.ExternalException ee
                        ? $"0x{ee.HResult:X8}"
                        : $"0x{cur.HResult:X8}",
            message = cur.Message,
        });
    }

    // {@ExceptionChain} destructures the list as a structured property, not a string.
    using (Serilog.Context.LogContext.PushProperty("ExceptionChain", chain, destructureObjects: true))
    {
        logger.LogWarning(ex, messageTemplate, args); // ex still attached for stack trace
    }
}
```

- The `@` operator in Serilog templates forces destructure. Pushing via `LogContext.PushProperty(name, value, destructureObjects: true)` achieves the same for scope properties. **VERIFIED** via Serilog docs.
- `DirectoryServicesCOMException` extends `COMException` which extends `ExternalException` — `HResult` is always the LDAP error.

## Recommended Patterns

### Pattern 1: Outer controller scope

```csharp
// PasswordController.PostAsync — wrap the body after ModelState/Levenshtein/reCAPTCHA gates
// but before _provider.PerformPasswordChangeAsync is called.
var traceId = Activity.Current?.TraceId.ToString() ?? "unknown";
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["Username"] = model.Username,
    ["TraceId"] = traceId,
    ["ClientIp"] = clientIp,
}))
{
    var error = await _provider.PerformPasswordChangeAsync(...);
    // … existing audit/email logic
}
```

Note: the `TraceId` scope key is redundant with the `LogContext.PushProperty("TraceId", …)` pushed by the middleware. Per CONTEXT.md §2, the scope still includes it — Serilog tolerates duplicate property pushes (last write wins, identical value).

### Pattern 2: Step envelope (provider method)

```csharp
private UserPrincipal? FindUser(PrincipalContext ctx, string input)
{
    using (_logger.BeginScope(new { Step = "user-lookup" }))
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogDebug("user-lookup: start");
        try
        {
            // … existing body …
            return result;
        }
        finally
        {
            _logger.LogDebug("user-lookup: complete duration={ElapsedMs}", sw.ElapsedMilliseconds);
        }
    }
}
```

Apply to: `FindUser`, `ValidateGroups`, `ValidateUserCredentials`, `ChangePasswordInternal`, the `Save()` call in `PerformPasswordChangeAsync` (wrap inline), `AcquirePrincipalContext`.

### Pattern 3: AD-context scope (one-shot after principal resolved)

```csharp
// In PerformPasswordChangeAsync, immediately after FindUser returns non-null:
using var adContext = _logger.BeginScope(new Dictionary<string, object>
{
    ["Domain"] = _options.DefaultDomain ?? "unknown",
    ["DomainController"] = principalContext.ConnectedServer ?? "unknown",
    ["IdentityType"] = _idType.ToString(),
    ["UserCannotChangePassword"] = userPrincipal.UserCannotChangePassword,
    ["LastPasswordSetUtc"] = userPrincipal.LastPasswordSet?.ToUniversalTime().ToString("o") ?? "null",
});
// All subsequent log calls in this method (success + failure) inherit these 5 properties.
```

## Test Strategy

### Recommended: handwritten `ListLogEventSink` in test project

**Rationale:** Zero new package dependencies; no Serilog version-graph risk; fits existing test style (NSubstitute + xUnit v3 assertions).

```csharp
// src/PassReset.Tests/Infrastructure/ListLogEventSink.cs (NEW)
using Serilog.Core;
using Serilog.Events;

internal sealed class ListLogEventSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();
    public void Emit(LogEvent logEvent) => Events.Add(logEvent);

    public IEnumerable<string> AllRendered(IFormatProvider? fp = null) =>
        Events.Select(e => e.RenderMessage(fp));

    public IEnumerable<object?> AllPropertyValues() =>
        Events.SelectMany(e => e.Properties.Values.Select(ScalarOrNull));

    private static object? ScalarOrNull(LogEventPropertyValue v) =>
        v is ScalarValue sv ? sv.Value : v.ToString();
}
```

Build a `Logger` in tests:

```csharp
var sink = new ListLogEventSink();
var seriLogger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .Enrich.FromLogContext()
    .WriteTo.Sink(sink)
    .CreateLogger();
var factory = new Serilog.Extensions.Logging.SerilogLoggerFactory(seriLogger, dispose: true);
var logger = factory.CreateLogger<PasswordChangeProvider>();
```

### Alternatives considered

| Approach | Pro | Con | Verdict |
|----------|-----|-----|---------|
| `Serilog.Sinks.XUnit` | Pipes Serilog output to xUnit test console | Another package reference; aimed at output, not capture | Skip |
| `Serilog.Sinks.TestCorrelator` | Purpose-built for test capture with LINQ-friendly API | Adds a package + namespace learning curve for a 30-line need | Skip |
| NSubstitute on `ILogger<T>` | Already used in codebase | Captures message template but NOT scope properties — fails for the AD-context scope assertion | Skip for redaction/chain tests; keep for unit tests that only care about "logger was called" |

### Required new test files

1. **`src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs`** — drives `DebugPasswordChangeProvider.PerformPasswordChangeAsync` with sentinel plaintext via `ListLogEventSink`, asserts no rendered message or property scalar contains either sentinel. Repeat for `LockoutPasswordChangeProvider` (wrap a sentinel-inspecting stub inner) and for `POST /api/password` via `WebApplicationFactory<Program>` (override the Serilog configuration in `IWebHostBuilder.ConfigureServices` using `builder.Host.UseSerilog((_, lc) => lc.WriteTo.Sink(sink))`).
2. **`src/PassReset.Tests/PasswordProvider/ExceptionChainLoggerTests.cs`** — unit tests on the static helper: 1-deep exception, 3-deep chain with `COMException` inner, correct `hresult`/`depth`/`type`/`message` shape, still attaches top-level `Exception` to Serilog for stack-trace emission.
3. **`src/PassReset.Tests/Infrastructure/ListLogEventSink.cs`** — helper, shared.

### WebApplicationFactory reuse for the sentinel test

The existing `DebugFactory` in `PasswordControllerTests.cs:21` is a good template. A redaction-variant factory overrides `ConfigureServices` to swap the Serilog logger for one that writes to a shared `ListLogEventSink`:

```csharp
public sealed class RedactionFactory : WebApplicationFactory<Program>
{
    public ListLogEventSink Sink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder b) =>
        b.UseSerilog((_, lc) => lc.MinimumLevel.Verbose()
                                  .Enrich.FromLogContext()
                                  .WriteTo.Sink(Sink));
}
```

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Correlation ID generation | Custom Guid emission | `Activity.Current.TraceId` | W3C standard; already propagated by ASP.NET Core hosting diagnostics |
| Request-scoped log enrichment | `AsyncLocal<string>` + manual cleanup | `Serilog.Context.LogContext.PushProperty` (IDisposable) | Already wired via `Enrich.FromLogContext()` |
| Nested log context | Flat `_logger.LogX(..., prop1, prop2,...)` on every call | `ILogger.BeginScope(anonymous)` | Serilog translates scopes into log properties automatically |
| Exception destructure | `ex.ToString()` into a single string property | Structured chain array `[{depth,type,hresult,message}...]` via `LogContext.PushProperty` with `destructureObjects: true` | Structured output is queryable in any log aggregator later; string blobs are not |
| Test log capture | Inspecting file sink output | `ILogEventSink` impl capturing `LogEvent` in a list | Direct access to rendered text AND structured properties, no file I/O |

## Common Pitfalls

### Pitfall 1: `LogContext.PushProperty` disposed before `await next()`
**What goes wrong:** Writing `LogContext.PushProperty("TraceId", …);` without `using` — property is pushed then immediately popped because the `IDisposable` is not held. Log entries in downstream handlers lack TraceId.
**How to avoid:** Always `using (LogContext.PushProperty(...))` bracketing the `await next(context)` call.

### Pitfall 2: `BeginScope` disposal before async completion
**What goes wrong:** `var scope = _logger.BeginScope(...);` without `using` — scope ends immediately, log properties don't flow to awaited work.
**How to avoid:** `using var scope = _logger.BeginScope(...);` or `using (_logger.BeginScope(...)) { await ... }`. Critical for async methods.

### Pitfall 3: `ConnectedServer` read before bind
**What goes wrong:** Reading `principalContext.ConnectedServer` before any AD query has executed returns null. A `FindUser` that returns null (user-not-found) may not have completed a successful bind on all code paths.
**How to avoid:** Set up the AD-context scope only **inside** the `if (userPrincipal != null)` branch in `PerformPasswordChangeAsync` (i.e. after line 46 but before the min-password-length check at line 48). The user-not-found Warning at `:43` logs without the AD context — acceptable because we know the DC answered (else `FindByIdentity` would have thrown).

### Pitfall 4: Sentinel test false pass
**What goes wrong:** Test builds `LoggerConfiguration()` with `MinimumLevel.Information` — Debug step events never reach the sink, and the redaction test passes vacuously even if a Debug message rendered the plaintext.
**How to avoid:** Always set `MinimumLevel.Verbose()` (or `Debug`) in the test logger config. Assert a positive control: the sink captured at least one Debug event from the flow under test.

### Pitfall 5: Scope property shadows an outer message template parameter
**What goes wrong:** Outer scope pushes `Username`; an inner `LogInformation("... {Username}", user)` binds a new Username to the template placeholder. Serilog dedupes by property name — the scope value is silently replaced for that event, and the outer's value is lost on subsequent events if the logger is misconfigured.
**How to avoid:** Once `Username` is in the outer scope, **drop the `{Username}` placeholder** from inner log messages — they'll inherit it automatically. This is the whole point of the scope-based refactor (CONTEXT.md §2 "no duplicate properties sprayed across 6+ messages").

### Pitfall 6: `LogExceptionChain` swallows inner null-ref
**What goes wrong:** `ex.GetType().Name` for a null `ex` NRE. Loop body needs guard if called defensively.
**How to avoid:** Signature `LogExceptionChain(ILogger, Exception)` — non-nullable `Exception`. Guard at caller if needed.

### Pitfall 7: Destructure of giant exception property bag
**What goes wrong:** Passing the raw `Exception` object with `{@Ex}` destructures every property — stack trace, TargetSite, Data, HelpLink — into a log event that can exceed the File-sink buffer.
**How to avoid:** Build the chain as a list of small anonymous objects (the pattern in §Library §6). The raw exception is still passed as the first arg to `LogWarning(ex, …)` for stack trace emission; Serilog's default exception renderer handles it compactly.

## Risk / Unknowns

1. **`Activity.Current` null in test host.** `WebApplicationFactory` uses the same hosting diagnostics as production, so `Activity.Current` will be populated — but if a test runs middleware outside `HttpContext` (e.g. direct `Invoke` call), `TraceId` will be `"unknown"`. Acceptable.
2. **`PrincipalContext.ConnectedServer` behavior under `UseAutomaticContext`.** Microsoft docs state it returns the DC hostname but do not guarantee non-null for all `ContextType` combinations. Plan: null-coalesce to `"unknown"` in the scope dictionary.
3. **Log file size.** Verbose-level step events at 2 per step × 6 steps × every password change ≈ 12 new Debug lines per request. At the documented prod default (Information), these are filtered at the sink level and cost nothing. No retention change needed (CONTEXT.md §Out of Scope).
4. **Lockout decorator + redaction.** `LockoutPasswordChangeProvider.PerformPasswordChangeAsync` accepts `currentPassword`/`newPassword` parameters by value but never logs them — verified at `LockoutPasswordChangeProvider.cs:95–127`. The sentinel test must still cover this path explicitly per CONTEXT.md §5.

## Project Constraints (from CLAUDE.md)

- Windows-only build target (`net10.0-windows`) — all new code lands in existing Windows-targeting projects.
- Commit convention: `fix(provider)`, `feat(web)`, `test(provider)` — type must be from the approved list; scope is one of `web provider common deploy docs ci deps security installer`.
- CI: `.github/workflows/tests.yml` runs `dotnet test` on `windows-latest` — new tests must pass there.
- No new runtime dependencies (CONTEXT.md §Out of Scope). `Serilog.AspNetCore 10.0.0` (already referenced) covers `LogContext.PushProperty`.
- Coverlet thresholds apply — new helper `ExceptionChainLogger` needs unit coverage.

## Files Likely Touched (confirmed vs. CONTEXT.md)

| File | Action | Cite |
|------|--------|------|
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` | Modify — step scopes, AD-context scope, route two catches through `LogExceptionChain`, add `PrincipalOperationException` catch | `:34–129`, `:396–443` |
| `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs` | Modify — add Debug event for counter increment (already Warning), optional start-of-sweep event | `:115–117`, `:218–219` |
| `src/PassReset.PasswordProvider/ExceptionChainLogger.cs` | **NEW** — static helper | — |
| `src/PassReset.Web/Controllers/PasswordController.cs` | Modify — wrap `PerformPasswordChangeAsync` call in outer `BeginScope` | `:144–215` |
| `src/PassReset.Web/Program.cs` | Modify — register/insert middleware between lines 201 and 250 | `:198–255` |
| `src/PassReset.Web/Middleware/TraceIdEnricherMiddleware.cs` | **NEW** — tiny middleware | — |
| `src/PassReset.Tests/Infrastructure/ListLogEventSink.cs` | **NEW** | — |
| `src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs` | **NEW** | — |
| `src/PassReset.Tests/PasswordProvider/ExceptionChainLoggerTests.cs` | **NEW** | — |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | ASP.NET Core 10's hosting middleware populates `Activity.Current` before any user middleware runs | §Library §1, Current State middleware ordering | TraceId would be null — mitigated by `?? "unknown"` coalesce |
| A2 | Serilog 10.0.0's `LogContext.PushProperty(name, value, destructureObjects:true)` emits the list as a structured array in the File sink JSON output | §Library §6 | Would emit as stringified list — still useful but less queryable |
| A3 | `PrincipalContext.ConnectedServer` returns non-null after a successful `FindByIdentity` call under `UseAutomaticContext = true` | §Library §4, Pitfall 3 | Null shows as `"unknown"` in logs — cosmetic only |

## Open Questions

1. **Should the `PrincipalOperationException` catch emit the structured chain or only targeted context?** — CONTEXT.md §3 explicitly limits the chain walker to two types. Plan should add a targeted `catch (PrincipalOperationException ex)` with distinct log fields (ErrorCode, HResult) but **not** use `LogExceptionChain`. Flag for planner confirmation.
2. **Should the step envelope include `Stopwatch.ElapsedMs`?** — CONTEXT.md §2 example shows `"complete duration={ElapsedMs}"`. Recommend yes, as a single scalar; no additional tooling needed.

## Sources

### Primary (HIGH)
- Microsoft Learn — `System.Diagnostics.Activity`, `ActivityTraceId`, `System.DirectoryServices.AccountManagement.PrincipalContext.ConnectedServer`, `UserPrincipal.LastPasswordSet`
- Serilog project docs — `Enrich.FromLogContext`, `LogContext.PushProperty`, `Serilog.Extensions.Logging.SerilogLogger.BeginScope` source
- Codebase: `PasswordChangeProvider.cs`, `LockoutPasswordChangeProvider.cs`, `Program.cs`, `PasswordController.cs`, `PassReset.Tests/**`

### Secondary (MEDIUM)
- xUnit v3 3.2.2 migration notes (confirms compatibility with `WebApplicationFactory<Program>` on ASP.NET Core 10)

## Metadata

- Confidence: Standard-stack HIGH (all libraries already in repo), Architecture HIGH (diagnostic refactor, well-scoped), Pitfalls MEDIUM-HIGH (Serilog scope/context gotchas are real and well-documented)
- Research date: 2026-04-15
- Valid until: 2026-05-15 (stable libraries, no fast-moving ecosystem)
