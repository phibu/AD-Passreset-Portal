# Phase 11 Hygiene — Cross-Platform Seams Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unblock Linux deployment of `PassReset.Web` and enable the 7 currently-skipped Windows contract tests by introducing two narrow, purpose-built seams that decouple `System.DirectoryServices.AccountManagement` from cross-platform code.

**Architecture:** Two independent seams introduced in parallel:
1. **`IAdConnectivityProbe`** (in `PassReset.Common`) — narrow "is AD reachable?" health probe. Windows implementation uses `PrincipalContext` (domain-join check). LDAP implementation uses TCP probe on configured hostnames (same logic `HealthController` already has inline). Web host wires the right one by `ProviderMode`, mirroring the existing `IPasswordChangeProvider` selection.
2. **`IPrincipalContextFactory`** (in `PassReset.PasswordProvider`) — Windows-only seam for `PrincipalContext` + `UserPrincipal.FindByIdentity` that lets `PasswordChangeProviderTestFixture` inject a fake, unskipping the 7 `PasswordChangeProviderContractTests`.

These seams land together in one PR because they share a common framing (decoupling Windows-specific AD access) and both unblock post-Phase-11 known-limitations items documented in CHANGELOG.

**Tech Stack:** ASP.NET Core 10 + C# 13, xUnit v3 + NSubstitute, `System.DirectoryServices.AccountManagement` (Windows provider), existing `IExpiryServiceDiagnostics` + `ILockoutDiagnostics` pattern for DI-injected probes.

---

## File Structure

**New files (all in `PassReset.Common`):**
- `src/PassReset.Common/IAdConnectivityProbe.cs` — probe interface + result record

**New files (all in `PassReset.PasswordProvider`, Windows-only):**
- `src/PassReset.PasswordProvider/DomainJoinedProbe.cs` — `IAdConnectivityProbe` implementation using `PrincipalContext` (domain-join check)
- `src/PassReset.PasswordProvider/IPrincipalContextFactory.cs` — seam abstracting `new PrincipalContext(...)` + `UserPrincipal.FindByIdentity(ctx, ...)`
- `src/PassReset.PasswordProvider/DefaultPrincipalContextFactory.cs` — production `IPrincipalContextFactory` that calls the real BCL types

**New files (all in `PassReset.PasswordProvider.Ldap`, cross-platform):**
- `src/PassReset.PasswordProvider.Ldap/LdapTcpProbe.cs` — `IAdConnectivityProbe` implementation using TCP connect to `LdapHostnames` (same logic currently inlined in `HealthController.CheckAdConnectivityAsync`)

**New files (tests):**
- `src/PassReset.Tests/Services/LdapTcpProbeTests.cs` — cross-platform unit tests for `LdapTcpProbe`
- `src/PassReset.Tests.Windows/PasswordProvider/DomainJoinedProbeTests.cs` — Windows-only unit tests for `DomainJoinedProbe`
- `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderTestFixture.cs` — shared fixture for the Windows contract tests (new helper class that the skipped contract tests will consume)

**Files modified:**
- `src/PassReset.Web/Controllers/HealthController.cs` — replace inline `PrincipalContext` + TCP logic with `_probe.CheckAsync()` call; drop the `PrincipalContext` usage entirely (breaks the final Windows-API reference in Web)
- `src/PassReset.Web/Program.cs` — register `IAdConnectivityProbe` + `IPrincipalContextFactory` (Windows branch adds `IPrincipalContextFactory`; both branches add `IAdConnectivityProbe`)
- `src/PassReset.Web/PassReset.Web.csproj` — drop the conditional TFM (`net10.0-windows` on Windows, `net10.0` elsewhere) and target plain `net10.0` — this is the goal of the whole plan
- `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` — replace `new PrincipalContext(...)` + `UserPrincipal.FindByIdentity(...)` call sites with `_principalContextFactory.Create(...)` + `_principalContextFactory.FindUser(ctx, ...)`. Constructor gets a new `IPrincipalContextFactory` parameter.
- `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs` — remove all 7 `[Fact(Skip=SkipReason)]` overrides; implement `CreateProvider()` + `SeedUser()` via the new `PasswordChangeProviderTestFixture`
- `CHANGELOG.md` — add `[Unreleased]` entries for the two unblocks
- `docs/AD-ServiceAccount-LDAP-Setup.md` — drop "Linux deployment not yet supported" disclaimer once the Web project targets plain `net10.0`

---

### Task 1: Define `IAdConnectivityProbe` contract (Red phase)

**Files:**
- Create: `src/PassReset.Common/IAdConnectivityProbe.cs`

- [ ] **Step 1: Write the contract**

Create `src/PassReset.Common/IAdConnectivityProbe.cs`:

```csharp
namespace PassReset.Common;

/// <summary>
/// Narrow "is the directory reachable?" probe used by health endpoints. One
/// implementation per <see cref="ProviderMode"/>: <c>DomainJoinedProbe</c> for
/// Windows (PrincipalContext domain-join check) and <c>LdapTcpProbe</c> for
/// cross-platform deployments (TCP connect on configured LDAP hosts).
/// Implementations must not throw — failures are returned as <see cref="AdProbeStatus.Unhealthy"/>.
/// </summary>
public interface IAdConnectivityProbe
{
    Task<AdProbeResult> CheckAsync(CancellationToken cancellationToken = default);
}

public enum AdProbeStatus
{
    Healthy,
    Unhealthy,
    // "No AD configured" — e.g. debug-provider scenarios where LdapHostnames is empty.
    // HealthController treats this as healthy but surfaces it distinctly so operators can tell.
    NotConfigured,
}

public readonly record struct AdProbeResult(AdProbeStatus Status, long LatencyMs);
```

- [ ] **Step 2: Build to confirm compiles**

Run: `dotnet build src/PassReset.Common/PassReset.Common.csproj -c Release`
Expected: 0 errors. File is a contract with no implementations yet.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.Common/IAdConnectivityProbe.cs
git commit -m "feat(common): introduce IAdConnectivityProbe contract for health checks"
```

---

### Task 2: Implement `LdapTcpProbe` (TDD)

**Files:**
- Create: `src/PassReset.PasswordProvider.Ldap/LdapTcpProbe.cs`
- Create: `src/PassReset.Tests/Services/LdapTcpProbeTests.cs`

- [ ] **Step 1: Write failing tests**

Create `src/PassReset.Tests/Services/LdapTcpProbeTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;

namespace PassReset.Tests.Services;

public class LdapTcpProbeTests
{
    private static LdapTcpProbe Build(PasswordChangeOptions opts) =>
        new(Options.Create(opts), NullLogger<LdapTcpProbe>.Instance);

    [Fact]
    public async Task CheckAsync_NoHostnamesConfigured_ReturnsNotConfigured()
    {
        var probe = Build(new PasswordChangeOptions { LdapHostnames = Array.Empty<string>() });

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UnreachableHost_ReturnsUnhealthy()
    {
        // RFC 5737 TEST-NET-1: guaranteed unroutable. Probe should time out fast.
        var probe = Build(new PasswordChangeOptions
        {
            LdapHostnames = new[] { "192.0.2.1" },
            LdapPort      = 636,
        });

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CheckAsync_IgnoresBlankHostnames()
    {
        // Whitespace-only entries in LdapHostnames are skipped, not probed.
        var probe = Build(new PasswordChangeOptions
        {
            LdapHostnames = new[] { "", "  " },
            LdapPort      = 636,
        });

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.NotConfigured, result.Status);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (LdapTcpProbe does not exist)**

Run: `dotnet test src/PassReset.Tests/PassReset.Tests.csproj -c Release --filter "FullyQualifiedName~LdapTcpProbeTests"`
Expected: build fails with `CS0246: The type or namespace name 'LdapTcpProbe' could not be found`.

- [ ] **Step 3: Implement `LdapTcpProbe`**

Create `src/PassReset.PasswordProvider.Ldap/LdapTcpProbe.cs`:

```csharp
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Cross-platform AD connectivity probe. Attempts a TCP connect to each
/// configured <see cref="PasswordChangeOptions.LdapHostnames"/> entry on
/// <see cref="PasswordChangeOptions.LdapPort"/>. Returns <see cref="AdProbeStatus.Healthy"/>
/// as soon as any DC answers. Logic mirrors the pre-Phase-11-hygiene inline code
/// that lived in <c>HealthController.CheckAdConnectivityAsync</c>.
/// </summary>
public sealed class LdapTcpProbe : IAdConnectivityProbe
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<LdapTcpProbe> _logger;

    public LdapTcpProbe(IOptions<PasswordChangeOptions> options, ILogger<LdapTcpProbe> logger)
    {
        _options = options;
        _logger  = logger;
    }

    public async Task<AdProbeResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var sw   = Stopwatch.StartNew();

        var hostnames = (opts.LdapHostnames ?? Array.Empty<string>())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .ToArray();

        if (hostnames.Length == 0)
            return new AdProbeResult(AdProbeStatus.NotConfigured, sw.ElapsedMilliseconds);

        foreach (var host in hostnames)
        {
            try
            {
                using var cts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ConnectTimeout);
                using var client = new TcpClient();
                await client.ConnectAsync(host, opts.LdapPort, cts.Token);
                return new AdProbeResult(AdProbeStatus.Healthy, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AD connectivity probe failed for LDAP endpoint {Host}:{Port}",
                    host, opts.LdapPort);
            }
        }

        _logger.LogError(
            "AD connectivity probe failed — no LDAP endpoints reachable ({Hosts})",
            string.Join(", ", hostnames));
        return new AdProbeResult(AdProbeStatus.Unhealthy, sw.ElapsedMilliseconds);
    }
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test src/PassReset.Tests/PassReset.Tests.csproj -c Release --filter "FullyQualifiedName~LdapTcpProbeTests"`
Expected: 3/3 pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapTcpProbe.cs \
        src/PassReset.Tests/Services/LdapTcpProbeTests.cs
git commit -m "feat(provider-ldap): LdapTcpProbe — cross-platform AD reachability probe"
```

---

### Task 3: Implement `DomainJoinedProbe` (TDD, Windows-only)

**Files:**
- Create: `src/PassReset.PasswordProvider/DomainJoinedProbe.cs`
- Create: `src/PassReset.Tests.Windows/PasswordProvider/DomainJoinedProbeTests.cs`

- [ ] **Step 1: Write failing test**

Create `src/PassReset.Tests.Windows/PasswordProvider/DomainJoinedProbeTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;

namespace PassReset.Tests.Windows.PasswordProvider;

public class DomainJoinedProbeTests
{
    [Fact]
    public async Task CheckAsync_UseAutomaticContextFalse_ReturnsNotConfigured()
    {
        // DomainJoinedProbe is only meaningful when UseAutomaticContext is true.
        // When false, the probe short-circuits to NotConfigured — the LDAP probe
        // should be used instead.
        var opts = new PasswordChangeOptions { UseAutomaticContext = false };
        var probe = new DomainJoinedProbe(
            Options.Create(opts),
            NullLogger<DomainJoinedProbe>.Instance);

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.NotConfigured, result.Status);
    }

    [Fact]
    public async Task CheckAsync_UseAutomaticContextTrue_NonDomainJoinedMachine_ReturnsUnhealthy()
    {
        // CI runners aren't domain-joined. Expect Unhealthy, not a thrown exception.
        var opts = new PasswordChangeOptions { UseAutomaticContext = true };
        var probe = new DomainJoinedProbe(
            Options.Create(opts),
            NullLogger<DomainJoinedProbe>.Instance);

        var result = await probe.CheckAsync();

        Assert.Equal(AdProbeStatus.Unhealthy, result.Status);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj -c Release --filter "FullyQualifiedName~DomainJoinedProbeTests"`
Expected: build fails with `CS0246` for `DomainJoinedProbe`.

- [ ] **Step 3: Implement `DomainJoinedProbe`**

Create `src/PassReset.PasswordProvider/DomainJoinedProbe.cs`:

```csharp
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;

namespace PassReset.PasswordProvider;

/// <summary>
/// Windows-only AD connectivity probe. Opens a <see cref="PrincipalContext"/>
/// in <see cref="ContextType.Domain"/> mode and verifies <c>ConnectedServer</c>
/// is non-null. Requires a domain-joined Windows host.
/// Returns <see cref="AdProbeStatus.NotConfigured"/> when
/// <see cref="PasswordChangeOptions.UseAutomaticContext"/> is false — the LDAP
/// probe should be wired in that case.
/// </summary>
public sealed class DomainJoinedProbe : IAdConnectivityProbe
{
    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<DomainJoinedProbe> _logger;

    public DomainJoinedProbe(
        IOptions<PasswordChangeOptions> options,
        ILogger<DomainJoinedProbe> logger)
    {
        _options = options;
        _logger  = logger;
    }

    public Task<AdProbeResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        var sw   = Stopwatch.StartNew();

        if (!opts.UseAutomaticContext)
            return Task.FromResult(new AdProbeResult(AdProbeStatus.NotConfigured, sw.ElapsedMilliseconds));

        try
        {
            using var ctx = new PrincipalContext(ContextType.Domain);
            var status = ctx.ConnectedServer != null
                ? AdProbeStatus.Healthy
                : AdProbeStatus.Unhealthy;
            return Task.FromResult(new AdProbeResult(status, sw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AD connectivity probe failed (automatic context)");
            return Task.FromResult(new AdProbeResult(AdProbeStatus.Unhealthy, sw.ElapsedMilliseconds));
        }
    }
}
```

- [ ] **Step 4: Run tests — expect green**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj -c Release --filter "FullyQualifiedName~DomainJoinedProbeTests"`
Expected: 2/2 pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider/DomainJoinedProbe.cs \
        src/PassReset.Tests.Windows/PasswordProvider/DomainJoinedProbeTests.cs
git commit -m "feat(provider): DomainJoinedProbe — Windows AD reachability probe"
```

---

### Task 4: Wire `IAdConnectivityProbe` into HealthController (TDD)

**Files:**
- Modify: `src/PassReset.Web/Controllers/HealthController.cs`
- Modify: `src/PassReset.Web/Program.cs`
- Modify: `src/PassReset.Tests.Windows/Web/Controllers/HealthControllerTests.cs` (existing — adapt tests if they assert specific probe internals)

- [ ] **Step 1: Read existing `HealthController.cs` to confirm the exact lines to replace**

Read: `src/PassReset.Web/Controllers/HealthController.cs`
Find: the `CheckAdConnectivityAsync()` method that contains both the `PrincipalContext` branch (`opts.UseAutomaticContext`) and the TCP-connect fallback loop.

- [ ] **Step 2: Replace the method body to delegate to the probe**

Edit `src/PassReset.Web/Controllers/HealthController.cs`:

Replace the whole `CheckAdConnectivityAsync()` method with a thin delegate that calls the injected `IAdConnectivityProbe` and maps the result:

```csharp
private async Task<(string status, long latencyMs)> CheckAdConnectivityAsync()
{
    var result = await _adProbe.CheckAsync(HttpContext.RequestAborted);
    var status = result.Status switch
    {
        AdProbeStatus.Healthy        => "healthy",
        AdProbeStatus.NotConfigured  => "healthy",   // debug/unconfigured scenarios
        _                            => "unhealthy",
    };
    return (status, result.LatencyMs);
}
```

Also add the new field + ctor parameter. At the top of the class (existing fields around lines 20-26 of the current file), add:

```csharp
private readonly IAdConnectivityProbe _adProbe;
```

And extend the constructor parameter list to include `IAdConnectivityProbe adProbe` and assign `_adProbe = adProbe;`.

Remove the now-unused imports at the top of the file:
- `using System.DirectoryServices.AccountManagement;` (if present)
- `using System.Net.Sockets;` (if not used elsewhere in the file — verify before removing)

Add at the top: `using PassReset.Common;`

- [ ] **Step 3: Register the probe in `Program.cs`**

Edit `src/PassReset.Web/Program.cs`. In the provider-selection block (the `if (webSettings.UseDebugProvider) { ... } else if (effectiveProvider == WiringTarget.Ldap) { ... } #if WINDOWS_PROVIDER else { ... } #endif` structure from Phase 11 Task 18), add the probe wiring:

- Inside the Debug branch: `builder.Services.AddSingleton<IAdConnectivityProbe, LdapTcpProbe>();` (debug mode still uses the LDAP probe for the health endpoint — it's cross-platform and harmless when `LdapHostnames` is empty; returns `NotConfigured`)
- Inside the Ldap branch: `builder.Services.AddSingleton<IAdConnectivityProbe, LdapTcpProbe>();`
- Inside the `#if WINDOWS_PROVIDER` Windows branch: `builder.Services.AddSingleton<IAdConnectivityProbe, DomainJoinedProbe>();`

Add the using at the top of `Program.cs`: `using PassReset.PasswordProvider.Ldap;` (already present from Phase 11). If `DomainJoinedProbe` lives in `PassReset.PasswordProvider`, also add `using PassReset.PasswordProvider;` — but since the registration is inside `#if WINDOWS_PROVIDER`, keep that using inside the `#if` too (C# allows `using` directives only at file scope, so you'll need to either add it at the top AND accept that the reference resolves only on Windows, OR fully-qualify the type at the registration site as `PassReset.PasswordProvider.DomainJoinedProbe`). Prefer fully-qualifying to keep the using list clean.

- [ ] **Step 4: Build and run affected tests**

Run:
```bash
dotnet build src/PassReset.sln -c Release
dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~HealthController"
```

Expected:
- Build passes with 0 errors.
- `HealthControllerTests` still pass. If any existing test asserted internal behaviors of `CheckAdConnectivityAsync` (e.g., verified a specific log message, or used NSubstitute on an internal collaborator), adapt the test to mock `IAdConnectivityProbe` instead. Don't widen the change; only adjust the minimum needed to make the existing assertions meaningful against the new indirection.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.Web/Controllers/HealthController.cs \
        src/PassReset.Web/Program.cs \
        src/PassReset.Tests.Windows/Web/Controllers/HealthControllerTests.cs
git commit -m "refactor(web): HealthController delegates AD check to IAdConnectivityProbe"
```

---

### Task 5: Retarget `PassReset.Web.csproj` to plain `net10.0`

This is the payoff step for Tasks 1-4: with `PrincipalContext` removed from `HealthController`, `PassReset.Web` no longer has a direct Windows-API dependency.

**Files:**
- Modify: `src/PassReset.Web/PassReset.Web.csproj`

- [ ] **Step 1: Read the current csproj**

Read: `src/PassReset.Web/PassReset.Web.csproj`
Find: the Phase 11 conditional `<TargetFramework>` pair (`net10.0-windows` on Windows, `net10.0` elsewhere) + the `<DefineConstants>$(DefineConstants);WINDOWS_PROVIDER</DefineConstants>` block + the conditional `<ProjectReference Include="..\PassReset.PasswordProvider\...">` guard.

- [ ] **Step 2: Simplify to plain `net10.0`**

Edit `src/PassReset.Web/PassReset.Web.csproj`:

- Replace the conditional `<TargetFramework>` pair with `<TargetFramework>net10.0</TargetFramework>` (drop the `-windows` variant and the `Condition="'$(OS)' == 'Windows_NT'"` attributes on both `<TargetFramework>` elements).
- Keep `<ItemGroup Condition="'$(OS)' == 'Windows_NT'"><ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" /></ItemGroup>` — the Windows provider is still Windows-only, so on Linux we skip the reference (debug + LDAP providers are still available from the unconditional references).
- Keep the `<PropertyGroup Condition="'$(OS)' == 'Windows_NT'"><DefineConstants>$(DefineConstants);WINDOWS_PROVIDER</DefineConstants></PropertyGroup>` block — `Program.cs` still uses `#if WINDOWS_PROVIDER` around the Windows branch of `ProviderMode` selection.

- [ ] **Step 3: Build on Windows**

Run: `dotnet build src/PassReset.sln -c Release`
Expected: 0 errors. This is the critical validation point — if `HealthController` or any other Web file still references a Windows-only type that wasn't guarded, the build will fail here with a hard error (not a warning). Fix before proceeding.

- [ ] **Step 4: Run full test suite — no regressions**

Run: `dotnet test src/PassReset.sln -c Release`
Expected: 232 passed + 8 skipped (same counts as Phase 11 merge; 7 contract skips get fixed in Task 9). No new failures.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.Web/PassReset.Web.csproj
git commit -m "build(web): target plain net10.0 — cross-platform build unblocked"
```

---

### Task 6: Define `IPrincipalContextFactory` contract (Red phase)

**Files:**
- Create: `src/PassReset.PasswordProvider/IPrincipalContextFactory.cs`

- [ ] **Step 1: Read `PasswordChangeProvider.cs` to find every `PrincipalContext` + `UserPrincipal.FindByIdentity` call site**

Run: `grep -n "PrincipalContext\|UserPrincipal\.FindByIdentity" src/PassReset.PasswordProvider/PasswordChangeProvider.cs`

Expected output: 4 call sites plus 3 `FindByIdentity` lines (per the Phase 11 final review notes). Note the line numbers — these are the sites Task 7 will refactor.

- [ ] **Step 2: Write the interface based on what those call sites actually need**

Create `src/PassReset.PasswordProvider/IPrincipalContextFactory.cs`:

```csharp
using System.DirectoryServices.AccountManagement;

namespace PassReset.PasswordProvider;

/// <summary>
/// Seam abstracting <see cref="PrincipalContext"/> + <see cref="UserPrincipal.FindByIdentity(PrincipalContext, string)"/>
/// so that contract tests can inject a fake. Scope is deliberately minimal —
/// mirrors the two AD operations <see cref="PasswordChangeProvider"/> actually
/// performs. Not a general-purpose `System.DirectoryServices` wrapper.
/// </summary>
public interface IPrincipalContextFactory
{
    /// <summary>
    /// Creates a <see cref="PrincipalContext"/> in <see cref="ContextType.Domain"/> mode.
    /// The caller is responsible for disposing the returned context.
    /// </summary>
    /// <param name="container">Optional OU/container DN (null = domain root).</param>
    /// <param name="username">Optional explicit bind username; null = automatic/current-user context.</param>
    /// <param name="password">Optional explicit bind password; required when username is non-null.</param>
    PrincipalContext CreateDomainContext(string? container = null, string? username = null, string? password = null);

    /// <summary>
    /// Resolves a user by <paramref name="identityType"/> + <paramref name="identityValue"/>.
    /// Returns null when no match. Caller is responsible for disposing the returned principal.
    /// </summary>
    UserPrincipal? FindUser(PrincipalContext context, IdentityType identityType, string identityValue);
}
```

- [ ] **Step 3: Build to confirm compiles**

Run: `dotnet build src/PassReset.PasswordProvider/PassReset.PasswordProvider.csproj -c Release`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PassReset.PasswordProvider/IPrincipalContextFactory.cs
git commit -m "feat(provider): IPrincipalContextFactory seam contract"
```

---

### Task 7: Implement `DefaultPrincipalContextFactory` (production)

**Files:**
- Create: `src/PassReset.PasswordProvider/DefaultPrincipalContextFactory.cs`

- [ ] **Step 1: Implement the trivial pass-through**

Create `src/PassReset.PasswordProvider/DefaultPrincipalContextFactory.cs`:

```csharp
using System.DirectoryServices.AccountManagement;

namespace PassReset.PasswordProvider;

/// <summary>
/// Production <see cref="IPrincipalContextFactory"/>: calls the real BCL types.
/// Contains no logic — exists only to satisfy DI without widening the public API
/// of <see cref="PasswordChangeProvider"/>.
/// </summary>
public sealed class DefaultPrincipalContextFactory : IPrincipalContextFactory
{
    public PrincipalContext CreateDomainContext(string? container = null, string? username = null, string? password = null)
    {
        // PrincipalContext ctor overloads differ by parameter count; pick the right one
        // to avoid passing nulls where the overload doesn't accept them.
        if (username is not null)
            return new PrincipalContext(ContextType.Domain, name: null, container: container, userName: username, password: password);
        if (container is not null)
            return new PrincipalContext(ContextType.Domain, name: null, container: container);
        return new PrincipalContext(ContextType.Domain);
    }

    public UserPrincipal? FindUser(PrincipalContext context, IdentityType identityType, string identityValue) =>
        UserPrincipal.FindByIdentity(context, identityType, identityValue);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.PasswordProvider/PassReset.PasswordProvider.csproj -c Release`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.PasswordProvider/DefaultPrincipalContextFactory.cs
git commit -m "feat(provider): DefaultPrincipalContextFactory — production pass-through"
```

---

### Task 8: Thread `IPrincipalContextFactory` through `PasswordChangeProvider`

This is the invasive step. Touch every `new PrincipalContext(...)` and `UserPrincipal.FindByIdentity(...)` site.

**Files:**
- Modify: `src/PassReset.PasswordProvider/PasswordChangeProvider.cs`
- Modify: `src/PassReset.Web/Program.cs` (register `IPrincipalContextFactory` in the Windows branch)
- Modify: existing tests under `src/PassReset.Tests.Windows/PasswordProvider/` that construct `PasswordChangeProvider` directly — they now need to pass a factory (use `DefaultPrincipalContextFactory` or a mock).

- [ ] **Step 1: Add the factory field + ctor parameter**

Edit `src/PassReset.PasswordProvider/PasswordChangeProvider.cs`:

Add at the top of the class (near `_options`, `_logger`, `_pwnedChecker`):

```csharp
private readonly IPrincipalContextFactory _contextFactory;
```

Extend the constructor:

```csharp
public PasswordChangeProvider(
    ILogger<PasswordChangeProvider> logger,
    IOptions<PasswordChangeOptions> options,
    PwnedPasswordChecker pwnedChecker,
    IPrincipalContextFactory contextFactory)   // NEW parameter — add as the last positional arg
{
    _logger  = logger;
    _options = options.Value;
    _pwnedChecker = pwnedChecker;
    _contextFactory = contextFactory;           // NEW assignment
    SetIdType();
}
```

- [ ] **Step 2: Replace every `new PrincipalContext(...)` call site**

Run: `grep -n "new PrincipalContext" src/PassReset.PasswordProvider/PasswordChangeProvider.cs`

For each hit, replace the `new PrincipalContext(ContextType.Domain, ...)` expression with `_contextFactory.CreateDomainContext(...)` — matching the original arguments one-to-one. Examples:

- `new PrincipalContext(ContextType.Domain)` → `_contextFactory.CreateDomainContext()`
- `new PrincipalContext(ContextType.Domain, name: null, container: ou)` → `_contextFactory.CreateDomainContext(container: ou)`
- `new PrincipalContext(ContextType.Domain, name: null, container: ou, userName: user, password: pw)` → `_contextFactory.CreateDomainContext(container: ou, username: user, password: pw)`

Keep the `using var ctx = ...` pattern — `IPrincipalContextFactory.CreateDomainContext` returns `PrincipalContext` which is still `IDisposable`.

- [ ] **Step 3: Replace every `UserPrincipal.FindByIdentity(ctx, ...)` call site**

Run: `grep -n "UserPrincipal\.FindByIdentity" src/PassReset.PasswordProvider/PasswordChangeProvider.cs`

Replace each `UserPrincipal.FindByIdentity(ctx, identityType, value)` with `_contextFactory.FindUser(ctx, identityType, value)`.

- [ ] **Step 4: Update existing tests that instantiate `PasswordChangeProvider` directly**

Run: `grep -rn "new PasswordChangeProvider(" src/PassReset.Tests.Windows/`

For each occurrence, add a `DefaultPrincipalContextFactory()` argument to the constructor call. Tests that need to mock AD should instead use a mock `IPrincipalContextFactory` (NSubstitute) — but defer that decision until Task 9 where the contract-test fixture gets built. For now, passing the real factory keeps the existing tests running (they already bind against the real AD — or more likely, they already skip / short-circuit when no DC is available).

- [ ] **Step 5: Register `IPrincipalContextFactory` in `Program.cs`**

Edit `src/PassReset.Web/Program.cs`. Inside the `#if WINDOWS_PROVIDER` Windows branch, add:

```csharp
builder.Services.AddSingleton<PassReset.PasswordProvider.IPrincipalContextFactory,
                              PassReset.PasswordProvider.DefaultPrincipalContextFactory>();
```

(Use fully-qualified names to keep using-directive changes minimal, matching the pattern from Task 4.)

- [ ] **Step 6: Build + full test suite**

Run:
```bash
dotnet build src/PassReset.sln -c Release
dotnet test src/PassReset.sln -c Release
```

Expected: 0 build errors. Test count still 232 passed + 8 skipped (the 7 contract skips haven't been fixed yet).

- [ ] **Step 7: Commit**

```bash
git add src/PassReset.PasswordProvider/PasswordChangeProvider.cs \
        src/PassReset.Web/Program.cs \
        src/PassReset.Tests.Windows/
git commit -m "refactor(provider): thread IPrincipalContextFactory through PasswordChangeProvider"
```

---

### Task 9: Unskip the 7 Windows contract tests

Build a fake `IPrincipalContextFactory` + companion fixture that lets the contract tests seed users. Then remove every `[Fact(Skip = SkipReason)]`.

**Files:**
- Create: `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderTestFixture.cs`
- Create: `src/PassReset.Tests.Windows/Fakes/FakePrincipalContextFactory.cs`
- Modify: `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs`

- [ ] **Step 1: Read the existing skipped contract file**

Read: `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs`
Confirm: the class derives from `IPasswordChangeProviderContract` (the base abstract class), has a `SkipReason` const, and 7 `[Fact(Skip = SkipReason)]` overrides.

Also read the base: `src/PassReset.Tests/Contracts/IPasswordChangeProviderContract.cs`
Confirm: `CreateProvider()` returns `IPasswordChangeProvider`; `SeedUser(string username, string currentPassword)` returns `TestUser`.

- [ ] **Step 2: Write the fake factory**

Create `src/PassReset.Tests.Windows/Fakes/FakePrincipalContextFactory.cs`:

```csharp
using System.DirectoryServices.AccountManagement;
using PassReset.PasswordProvider;

namespace PassReset.Tests.Windows.Fakes;

/// <summary>
/// In-memory <see cref="IPrincipalContextFactory"/> for contract tests. Stores seeded
/// users keyed by lowercased username; <see cref="FindUser"/> matches case-insensitively
/// against sAMAccountName / UPN / mail depending on <see cref="IdentityType"/>.
/// Context objects are intentionally null — the contract tests never deref them; the
/// production code only passes them back through <see cref="FindUser"/>.
/// </summary>
public sealed class FakePrincipalContextFactory : IPrincipalContextFactory
{
    private readonly Dictionary<string, (string currentPassword, bool userCannotChangePassword, bool disabled)> _users =
        new(StringComparer.OrdinalIgnoreCase);

    public void SeedUser(string username, string currentPassword,
        bool userCannotChangePassword = false, bool disabled = false)
    {
        _users[username] = (currentPassword, userCannotChangePassword, disabled);
    }

    // Null is safe here — production code stores the context in a `using var` but only
    // passes it back to FindUser/Password change. The fake never actually uses it.
    public PrincipalContext CreateDomainContext(string? container = null, string? username = null, string? password = null)
        => null!;

    public UserPrincipal? FindUser(PrincipalContext context, IdentityType identityType, string identityValue)
    {
        if (!_users.ContainsKey(identityValue))
            return null;
        // UserPrincipal is sealed in the BCL for .NET 10 — we cannot subclass it. The
        // contract test base only exercises PerformPasswordChangeAsync + GetUserEmail etc.
        // which in turn call ChangePassword / SetPassword on the returned principal.
        // That means this fake is insufficient if the contract tests reach into the
        // returned UserPrincipal. If tests fail here with NullReferenceException,
        // the contract should be tightened to not need a UserPrincipal at all, or
        // the seam widened to abstract the ChangePassword/SetPassword operations too.
        //
        // For Phase 11 hygiene, we accept this limitation and require that all contract
        // tests go through IPasswordChangeProvider's public interface, which in the
        // Windows provider already catches exceptions from UserPrincipal operations.
        throw new NotSupportedException(
            "FakePrincipalContextFactory does not return UserPrincipal instances. " +
            "Contract tests that require deref'ing UserPrincipal need a wider seam. " +
            "See docs/superpowers/plans/2026-04-21-phase-11-hygiene-cross-platform-seams.md Task 9.");
    }
}
```

**IMPORTANT — read before running tests:** `UserPrincipal` is a sealed BCL type that cannot be faked by subclassing. If the contract tests actually need to call methods on the returned `UserPrincipal` (e.g. `principal.ChangePassword(...)`), `FakePrincipalContextFactory.FindUser` throwing will fail the tests loudly. In that case the correct response is **not** to widen the fake — it's to **widen the seam**: add `ChangePasswordAsync(UserPrincipal user, string oldPw, string newPw)` (and similar) to `IPrincipalContextFactory` so the fake can implement the AD operation directly without returning a `UserPrincipal`.

That scope expansion would warrant a separate task. For now, proceed optimistically; if Step 5 fails with `NotSupportedException`, stop and escalate with a `DONE_WITH_CONCERNS` report so the plan can be expanded.

- [ ] **Step 3: Build the fixture that wires the fake into the contract**

Create `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderTestFixture.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Tests.Windows.Fakes;

namespace PassReset.Tests.Windows.Contracts;

/// <summary>
/// Shared test fixture used by the contract tests. Holds the fake factory, builds
/// a provider wired to it, and surfaces the seed operation the base class calls.
/// </summary>
internal sealed class PasswordChangeProviderTestFixture
{
    private readonly FakePrincipalContextFactory _fake = new();

    public PasswordChangeProvider BuildProvider()
    {
        var opts = new PasswordChangeOptions
        {
            UseAutomaticContext = true,
            AllowedUsernameAttributes = new[] { "samaccountname", "userprincipalname", "mail" },
        };
        var pwned = new PwnedPasswordChecker(
            new HttpClient(),
            NullLogger<PwnedPasswordChecker>.Instance);
        return new PasswordChangeProvider(
            NullLogger<PasswordChangeProvider>.Instance,
            Options.Create(opts),
            pwned,
            _fake);
    }

    public void SeedUser(string username, string currentPassword) =>
        _fake.SeedUser(username, currentPassword);
}
```

**Note:** `PwnedPasswordChecker` is a real dependency — it makes an outbound HTTP call. If the contract tests trigger a HIBP lookup, this will hit the network. Check whether the contract's `WeakPassword` test supplies a password that would be looked up; if yes, either mock `HttpClient` via `FakeHttpMessageHandler` (already in `src/PassReset.Tests.Windows/Fakes/`) or choose a test password that short-circuits the HIBP logic.

- [ ] **Step 4: Remove the Skip attributes and wire up the fixture**

Edit `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs`:

Replace the entire class body. The new shape:

```csharp
using PassReset.Common;
using PassReset.Tests.Contracts;

namespace PassReset.Tests.Windows.Contracts;

public sealed class PasswordChangeProviderContractTests : IPasswordChangeProviderContract
{
    private readonly PasswordChangeProviderTestFixture _fixture = new();

    protected override IPasswordChangeProvider CreateProvider() => _fixture.BuildProvider();

    protected override TestUser SeedUser(string username, string currentPassword)
    {
        _fixture.SeedUser(username, currentPassword);
        return new TestUser(username, currentPassword);
    }

    // No overrides with [Fact(Skip=...)] — inherit the base class's seven [Fact] methods directly.
}
```

- [ ] **Step 5: Run contract tests — expect green**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj -c Release --filter "FullyQualifiedName~PasswordChangeProviderContractTests"`

Expected: 7/7 pass.

If any test fails with `NotSupportedException` thrown from `FakePrincipalContextFactory.FindUser`, STOP and report `DONE_WITH_CONCERNS`: the fake must be upgraded OR the seam widened (see Step 2 IMPORTANT note). Do not paper over the failure.

If tests fail because the contract's `WeakPassword` scenario triggers HIBP lookups, tighten `PasswordChangeProviderTestFixture.BuildProvider` to inject a `FakeHttpMessageHandler` into `PwnedPasswordChecker`'s `HttpClient`.

- [ ] **Step 6: Run full suite — no regressions**

Run: `dotnet test src/PassReset.sln -c Release`

Expected: 239 passed + 1 skipped (the 7 Windows contract tests go from skipped → passed; the 1 remaining skip is the integration-tests-ldap gate on `PASSRESET_INTEGRATION_LDAP=1`).

- [ ] **Step 7: Commit**

```bash
git add src/PassReset.Tests.Windows/Contracts/ \
        src/PassReset.Tests.Windows/Fakes/FakePrincipalContextFactory.cs
git commit -m "test(contracts): unskip Windows contract tests via IPrincipalContextFactory fake"
```

---

### Task 10: Update CHANGELOG + LDAP setup doc

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/AD-ServiceAccount-LDAP-Setup.md`

- [ ] **Step 1: Read current CHANGELOG to locate insertion point**

Read: `CHANGELOG.md`
Find: the `## [Unreleased]` section at the top (added by Phase 11 Task 22).

- [ ] **Step 2: Add the hygiene entries**

Edit `CHANGELOG.md`. Under `## [Unreleased]`, add (or extend if the section is empty):

```markdown
### Added
- **`IAdConnectivityProbe`** — narrow AD-reachability seam for the health endpoint. `DomainJoinedProbe` (Windows) + `LdapTcpProbe` (cross-platform) implementations. Replaces the inline `PrincipalContext` / `TcpClient` logic in `HealthController`. *(web, provider, provider-ldap)*
- **`IPrincipalContextFactory`** — Windows-only seam over `PrincipalContext` + `UserPrincipal.FindByIdentity`. Enables the 7 previously-skipped `PasswordChangeProviderContractTests` in `PassReset.Tests.Windows`. *(provider, test)*

### Changed
- **`PassReset.Web` targets plain `net10.0`** — no longer uses the Phase-11 conditional TFM bridge. Windows provider remains conditionally referenced via `$(OS) == 'Windows_NT'` so the Linux build skips it. Cross-platform deployment of the web host is now supported. *(web)*

### Non-changes (explicit)
- **Windows provider still Windows-only.** `PassReset.PasswordProvider` continues to target `net10.0-windows`. Only the web host and the LDAP provider compile on Linux.
```

- [ ] **Step 3: Remove the "Linux deployment not yet supported" disclaimer from the alpha.1 section**

Still editing `CHANGELOG.md`. Find the `## [2.0.0-alpha.1] — TBD (release day)` section, then the `### Known Limitations` subsection Phase 11 Task 22 added. Edit the Linux-deployment bullet to reflect the new reality — either remove it entirely, or retitle it to describe what's STILL limited (e.g., the Windows provider itself remains Windows-only, but the web host and LDAP provider are Linux-buildable as of Unreleased).

Preferred wording for the updated bullet:

```markdown
- **Linux deployment is only available post-alpha.1.** Alpha.1 shipped with `PassReset.Web` using a conditional TFM (Windows on Windows, cross-platform elsewhere) because `HealthController` still required `PrincipalContext`. The follow-up in `[Unreleased]` introduced `IAdConnectivityProbe` + `IPrincipalContextFactory` seams, which unblocked plain `net10.0` targeting. Upgrade to the next release for Linux hosting support.
```

- [ ] **Step 4: Update the LDAP setup doc**

Read: `docs/AD-ServiceAccount-LDAP-Setup.md`

Find: any section or callout that says "currently only Windows hosts can run PassReset" or similar (added by Phase 11 Task 21). Update it to reflect that cross-platform hosting is now available and point at the changelog entry.

If no such disclaimer exists, skip this step — the doc was written forward-compatible.

- [ ] **Step 5: Commit**

```bash
git add CHANGELOG.md docs/AD-ServiceAccount-LDAP-Setup.md
git commit -m "docs: announce IAdConnectivityProbe + IPrincipalContextFactory seams (cross-platform web host)"
```

---

### Task 11: Final verification

**Files:** none modified.

- [ ] **Step 1: Full solution build + test**

Run:
```bash
dotnet build src/PassReset.sln -c Release
dotnet test src/PassReset.sln -c Release
```

Expected:
- 0 build errors, 0 warnings-as-errors
- Test counts: 239 passed + 1 skipped (was 232 + 8 before this phase; the 7 contract skips were unskipped)
- No new warnings from nullable, obsolete APIs, or CodeQL-tracked patterns

- [ ] **Step 2: Verify the csproj reality matches the claim**

Run: `grep -A2 "TargetFramework" src/PassReset.Web/PassReset.Web.csproj`
Expected: single line `<TargetFramework>net10.0</TargetFramework>` — no conditional variants.

- [ ] **Step 3: Verify no `System.DirectoryServices.AccountManagement` references remain in `PassReset.Web`**

Run: `grep -rn "System\.DirectoryServices\.AccountManagement\|PrincipalContext\|UserPrincipal" src/PassReset.Web/`
Expected: empty output. Any hit means Task 4 missed a call site; fix before committing.

- [ ] **Step 4: Verify the 7 contract tests passed on Windows CI**

Not a local command — this is a reminder for when the PR runs CI. The `tests / tests` job on Windows should show `PasswordChangeProviderContractTests` with 7 passing, 0 skipped. If any remain skipped, the fixture or fake needs a second pass (see Task 9 Step 2 note).

- [ ] **Step 5: Nothing to commit (verification only)**

If everything checks out, proceed to `superpowers:finishing-a-development-branch` to decide on merge/PR/cleanup.

---

## Self-Review Results

**Spec coverage:** Two unblocks promised, both covered:
1. HealthController Linux-unblock → Tasks 1, 2, 3, 4, 5
2. Windows contract tests unskip → Tasks 6, 7, 8, 9
Docs + verify → Tasks 10, 11.

**Placeholder scan:** None. Every code step has concrete code; every bash step has the exact command and expected output.

**Type consistency:**
- `IAdConnectivityProbe.CheckAsync` returns `Task<AdProbeResult>` — consistent across Tasks 1, 2, 3, 4.
- `AdProbeStatus` enum values (`Healthy` / `Unhealthy` / `NotConfigured`) — consistent.
- `IPrincipalContextFactory.CreateDomainContext` signature — consistent across Tasks 6, 7, 8, 9.
- `FakePrincipalContextFactory.FindUser` throwing `NotSupportedException` vs production returning `UserPrincipal?` — intentional divergence, documented in Task 9 Step 2.

**Known risk:** Task 9 Step 2 flags that `UserPrincipal` is sealed; the fake throws on `FindUser`. If the contract tests dereference the returned principal, Step 5 will fail and the plan needs extension. Plan explicitly calls this out as a `DONE_WITH_CONCERNS` escalation point rather than masking it.
