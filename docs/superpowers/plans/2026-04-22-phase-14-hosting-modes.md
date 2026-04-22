# Phase 14 — Hosting Modes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make IIS optional on Windows by adding a `Windows Service` hosting mode with Kestrel-terminated TLS, preserving the existing IIS path, and adding a `Console` mode for development. Operators pick a mode at install time; upgrades default to their current mode.

**Architecture:** Single `PassReset.exe` binary runs under IIS (via ASP.NET Core Module), as a Windows Service (via `Microsoft.Extensions.Hosting.WindowsServices`), or as a console app. Mode is an install-time decision driven by `Install-PassReset.ps1 -HostingMode IIS|Service|Console`. TLS in Service mode is configured via `Kestrel:HttpsCert` in `appsettings.Production.json` (thumbprint-from-store OR PFX file). Preflight dry-run keeps IIS untouched on failed migrations.

**Tech Stack:** ASP.NET Core 10, `Microsoft.Extensions.Hosting.WindowsServices`, xUnit v3, PowerShell 5.1+, Pester 5.x (new test framework for this repo), Windows Service Control Manager (SCM).

**Spec:** `docs/superpowers/specs/2026-04-22-phase-14-hosting-modes-design.md`

---

## File Structure

### New files (production)

| Path | Responsibility |
|---|---|
| `src/PassReset.Web/Services/Hosting/HostingMode.cs` | `enum HostingMode { Iis, Service, Console }` |
| `src/PassReset.Web/Services/Hosting/HostingModeDetector.cs` | Detects current mode at startup for logging |
| `src/PassReset.Web/Configuration/KestrelHttpsCertOptions.cs` | Options record for TLS cert (Thumbprint OR PfxPath) |
| `src/PassReset.Web/Models/KestrelHttpsCertOptionsValidator.cs` | `IValidateOptions<KestrelHttpsCertOptions>` |

### New files (tests)

| Path | Responsibility |
|---|---|
| `src/PassReset.Tests.Windows/Services/Hosting/HostingModeDetectorTests.cs` | Unit tests for detector |
| `src/PassReset.Tests.Windows/Configuration/KestrelHttpsCertOptionsValidatorTests.cs` | Unit tests for validator |
| `deploy/Install-PassReset.Tests.ps1` | First Pester file in the repo — installer param + preflight tests |

### New files (docs)

| Path | Responsibility |
|---|---|
| `docs/Deployment.md` | Consolidated deployment guide (IIS + Service + Console + cert + migration) |

### Modified files

| Path | Change |
|---|---|
| `src/PassReset.Web/PassReset.Web.csproj` | Add `Microsoft.Extensions.Hosting.WindowsServices` package |
| `src/PassReset.Web/Program.cs` | Add `.UseWindowsService()`, Kestrel cert config (Service mode), hosting-mode startup log, options registration |
| `src/PassReset.Web/appsettings.json` | Seed `Kestrel:HttpsCert` block (null defaults) |
| `src/PassReset.Web/appsettings.Production.template.json` | Seed same (no JSONC comments — Template_Has_Zero_Json_Comments policy) |
| `deploy/Install-PassReset.ps1` | Add `-HostingMode`, preflight, Service-mode branch; IIS branch structurally unchanged |
| `deploy/Uninstall-PassReset.ps1` | Handle Service uninstall in addition to IIS teardown |
| `docs/IIS-Setup.md` | Reduce to stub pointing at `docs/Deployment.md#iis-mode` |
| `docs/appsettings-Production.md` | Add `Kestrel:HttpsCert` section |
| `CLAUDE.md` | Add `KestrelHttpsCertOptions` to "Configuration keys to know" |
| `CHANGELOG.md` | Phase 14 entry under `[Unreleased]` |

---

## Task Sequencing

Inside-out: enum + detector → options + validator → appsettings seed → Program.cs wiring → installer preflight → installer Service branch → installer upgrade + Uninstall → verification → docs → CHANGELOG + regression.

### Task 1: Add `Microsoft.Extensions.Hosting.WindowsServices` package

**Files:**
- Modify: `src/PassReset.Web/PassReset.Web.csproj`

- [ ] **Step 1: Read the existing csproj**

Read `src/PassReset.Web/PassReset.Web.csproj`. Find the `<ItemGroup>` that lists `<PackageReference>` items (MailKit, Serilog.AspNetCore, Serilog.Sinks.File).

- [ ] **Step 2: Add the package reference**

Add inside the existing `<ItemGroup>` with other `PackageReference` entries:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="10.0.0" />
```

Version: match the ASP.NET Core major version already in use (10.0.0, same as `Serilog.AspNetCore`).

- [ ] **Step 3: Restore and build**

Run: `dotnet build src/PassReset.Web/PassReset.Web.csproj --configuration Release`
Expected: clean build (0 errors). Package downloads from NuGet.org.

- [ ] **Step 4: Commit**

```
git add src/PassReset.Web/PassReset.Web.csproj
git commit -m "chore(web): add Microsoft.Extensions.Hosting.WindowsServices package [phase-14]"
```

---

### Task 2: `HostingMode` enum

**Files:**
- Create: `src/PassReset.Web/Services/Hosting/HostingMode.cs`

- [ ] **Step 1: Create the enum**

Write `src/PassReset.Web/Services/Hosting/HostingMode.cs`:

```csharp
namespace PassReset.Web.Services.Hosting;

/// <summary>
/// Runtime hosting mode for the PassReset web process. Determined once at startup
/// and logged for observability. Does not change runtime behavior; operators pick
/// the mode at install time via <c>Install-PassReset.ps1 -HostingMode</c>.
/// </summary>
public enum HostingMode
{
    /// <summary>Hosted by IIS via the ASP.NET Core Module. TLS is terminated by IIS.</summary>
    Iis,

    /// <summary>Running as a Windows Service under SCM. Kestrel terminates TLS directly.</summary>
    Service,

    /// <summary>Running as a console process (dev / debugging). Kestrel terminates TLS if configured.</summary>
    Console,
}
```

- [ ] **Step 2: Build to confirm**

Run: `dotnet build src/PassReset.Web/PassReset.Web.csproj --configuration Release`
Expected: clean.

- [ ] **Step 3: Commit**

```
git add src/PassReset.Web/Services/Hosting/HostingMode.cs
git commit -m "feat(web): add HostingMode enum [phase-14]"
```

---

### Task 3: `HostingModeDetector` — failing tests

**Files:**
- Create: `src/PassReset.Tests.Windows/Services/Hosting/HostingModeDetectorTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `src/PassReset.Tests.Windows/Services/Hosting/HostingModeDetectorTests.cs`:

```csharp
using PassReset.Web.Services.Hosting;

namespace PassReset.Tests.Windows.Services.Hosting;

public sealed class HostingModeDetectorTests
{
    [Fact]
    public void Detect_WithIisEnvironmentVariable_ReturnsIis()
    {
        // ASP.NET Core Module sets ASPNETCORE_IIS_HTTPAUTH when hosted under IIS.
        var detector = new HostingModeDetector(
            isWindowsService: () => false,
            getEnv: name => name == "ASPNETCORE_IIS_HTTPAUTH" ? "windows;" : null);

        Assert.Equal(HostingMode.Iis, detector.Detect());
    }

    [Fact]
    public void Detect_AsWindowsService_ReturnsService()
    {
        var detector = new HostingModeDetector(
            isWindowsService: () => true,
            getEnv: _ => null);

        Assert.Equal(HostingMode.Service, detector.Detect());
    }

    [Fact]
    public void Detect_Default_ReturnsConsole()
    {
        var detector = new HostingModeDetector(
            isWindowsService: () => false,
            getEnv: _ => null);

        Assert.Equal(HostingMode.Console, detector.Detect());
    }

    [Fact]
    public void Detect_IisEnvVarPresentButEmpty_ReturnsConsole()
    {
        // Defensive: a blank string should not count as IIS.
        var detector = new HostingModeDetector(
            isWindowsService: () => false,
            getEnv: name => name == "ASPNETCORE_IIS_HTTPAUTH" ? "" : null);

        Assert.Equal(HostingMode.Console, detector.Detect());
    }
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj --filter FullyQualifiedName~HostingModeDetectorTests --configuration Release`
Expected: compile error — `HostingModeDetector` does not exist.

- [ ] **Step 3: Commit**

```
git add src/PassReset.Tests.Windows/Services/Hosting/HostingModeDetectorTests.cs
git commit -m "test(web): failing tests for HostingModeDetector [phase-14]"
```

---

### Task 4: `HostingModeDetector` — implementation

**Files:**
- Create: `src/PassReset.Web/Services/Hosting/HostingModeDetector.cs`

- [ ] **Step 1: Implement the detector**

Write `src/PassReset.Web/Services/Hosting/HostingModeDetector.cs`:

```csharp
using Microsoft.Extensions.Hosting.WindowsServices;

namespace PassReset.Web.Services.Hosting;

/// <summary>
/// Detects the active hosting mode at startup. Used for observability (Serilog
/// <c>Information</c> log) only — does not change runtime behavior.
/// Constructor seams make the detection logic unit-testable without depending on
/// the actual Windows environment.
/// </summary>
public sealed class HostingModeDetector
{
    private readonly Func<bool> _isWindowsService;
    private readonly Func<string, string?> _getEnv;

    public HostingModeDetector()
        : this(
            isWindowsService: WindowsServiceHelpers.IsWindowsService,
            getEnv: Environment.GetEnvironmentVariable)
    {
    }

    internal HostingModeDetector(Func<bool> isWindowsService, Func<string, string?> getEnv)
    {
        _isWindowsService = isWindowsService;
        _getEnv = getEnv;
    }

    public HostingMode Detect()
    {
        // Service check first — it's definitive when true.
        if (_isWindowsService()) return HostingMode.Service;

        // IIS sets ASPNETCORE_IIS_HTTPAUTH to a non-empty string (e.g. "windows;anonymous;").
        var iis = _getEnv("ASPNETCORE_IIS_HTTPAUTH");
        if (!string.IsNullOrEmpty(iis)) return HostingMode.Iis;

        return HostingMode.Console;
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj --filter FullyQualifiedName~HostingModeDetectorTests --configuration Release`
Expected: 4 passed, 0 failed.

- [ ] **Step 3: Commit**

```
git add src/PassReset.Web/Services/Hosting/HostingModeDetector.cs
git commit -m "feat(web): implement HostingModeDetector [phase-14]"
```

---

### Task 5: `KestrelHttpsCertOptions` record

**Files:**
- Create: `src/PassReset.Web/Configuration/KestrelHttpsCertOptions.cs`

- [ ] **Step 1: Create the options record**

Write `src/PassReset.Web/Configuration/KestrelHttpsCertOptions.cs`:

```csharp
namespace PassReset.Web.Configuration;

/// <summary>
/// Kestrel TLS certificate configuration. Used when <c>AdminSettings</c>/installer
/// selects <c>HostingMode.Service</c> — Kestrel binds HTTPS directly with this cert.
/// Ignored under IIS (IIS terminates TLS).
/// Exactly one of <see cref="Thumbprint"/> or <see cref="PfxPath"/> must be set in
/// Service mode; both null or both set is a validation error.
/// </summary>
public sealed class KestrelHttpsCertOptions
{
    /// <summary>SHA-1 thumbprint of a certificate in <see cref="StoreLocation"/>/<see cref="StoreName"/>.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Certificate store location. Defaults to <c>LocalMachine</c>. <c>CurrentUser</c> is not supported in Service mode.</summary>
    public string StoreLocation { get; set; } = "LocalMachine";

    /// <summary>Certificate store name. Defaults to <c>My</c> (Personal store).</summary>
    public string StoreName { get; set; } = "My";

    /// <summary>Absolute path to a PFX file. Mutually exclusive with <see cref="Thumbprint"/>.</summary>
    public string? PfxPath { get; set; }

    /// <summary>Password for the PFX file. Store via <c>SecretStore</c> (Phase 13) in production.</summary>
    public string? PfxPassword { get; set; }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.Web/PassReset.Web.csproj --configuration Release`
Expected: clean.

- [ ] **Step 3: Commit**

```
git add src/PassReset.Web/Configuration/KestrelHttpsCertOptions.cs
git commit -m "feat(web): add KestrelHttpsCertOptions record [phase-14]"
```

---

### Task 6: `KestrelHttpsCertOptionsValidator` — failing tests

**Files:**
- Create: `src/PassReset.Tests.Windows/Configuration/KestrelHttpsCertOptionsValidatorTests.cs`

- [ ] **Step 1: Write the failing test file**

Create `src/PassReset.Tests.Windows/Configuration/KestrelHttpsCertOptionsValidatorTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using PassReset.Web.Configuration;
using PassReset.Web.Models;
using PassReset.Web.Services.Hosting;

namespace PassReset.Tests.Windows.Configuration;

public sealed class KestrelHttpsCertOptionsValidatorTests
{
    private static ValidateOptionsResult Run(KestrelHttpsCertOptions opts, HostingMode mode) =>
        new KestrelHttpsCertOptionsValidator(() => mode).Validate(null, opts);

    [Fact]
    public void IisMode_BothNull_Passes()
    {
        // IIS terminates TLS; our cert options are ignored.
        var r = Run(new KestrelHttpsCertOptions(), HostingMode.Iis);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void ConsoleMode_BothNull_Passes()
    {
        // Console mode is dev / no-TLS; empty is acceptable.
        var r = Run(new KestrelHttpsCertOptions(), HostingMode.Console);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void ServiceMode_BothNull_Fails()
    {
        var r = Run(new KestrelHttpsCertOptions(), HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("Thumbprint", StringComparison.OrdinalIgnoreCase)
                                       || m.Contains("PfxPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceMode_ThumbprintOnly_Passes()
    {
        var r = Run(new KestrelHttpsCertOptions { Thumbprint = "ABCDEF" }, HostingMode.Service);
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void ServiceMode_PfxOnly_Passes()
    {
        var r = Run(new KestrelHttpsCertOptions
        {
            PfxPath = Path.Combine(Path.GetTempPath(), "cert.pfx"),
            PfxPassword = "pw",
        }, HostingMode.Service);
        Assert.True(r.Succeeded, string.Join("; ", r.Failures ?? []));
    }

    [Fact]
    public void ServiceMode_BothSet_Fails()
    {
        var r = Run(new KestrelHttpsCertOptions
        {
            Thumbprint = "ABCDEF",
            PfxPath = Path.Combine(Path.GetTempPath(), "cert.pfx"),
        }, HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("mutually exclusive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceMode_InvalidStoreLocation_Fails()
    {
        var r = Run(new KestrelHttpsCertOptions
        {
            Thumbprint = "ABCDEF",
            StoreLocation = "NotAStoreLocation",
        }, HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("StoreLocation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ServiceMode_CurrentUserStore_Fails()
    {
        // Service identity makes CurrentUser store non-portable; reject explicitly.
        var r = Run(new KestrelHttpsCertOptions
        {
            Thumbprint = "ABCDEF",
            StoreLocation = "CurrentUser",
        }, HostingMode.Service);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Failures!, m => m.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj --filter FullyQualifiedName~KestrelHttpsCertOptionsValidatorTests --configuration Release`
Expected: compile error — `KestrelHttpsCertOptionsValidator` does not exist.

- [ ] **Step 3: Commit**

```
git add src/PassReset.Tests.Windows/Configuration/KestrelHttpsCertOptionsValidatorTests.cs
git commit -m "test(web): failing tests for KestrelHttpsCertOptionsValidator [phase-14]"
```

---

### Task 7: `KestrelHttpsCertOptionsValidator` — implementation

**Files:**
- Create: `src/PassReset.Web/Models/KestrelHttpsCertOptionsValidator.cs`

- [ ] **Step 1: Implement the validator**

Write `src/PassReset.Web/Models/KestrelHttpsCertOptionsValidator.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using PassReset.Web.Configuration;
using PassReset.Web.Services.Hosting;

namespace PassReset.Web.Models;

/// <summary>
/// Validates <see cref="KestrelHttpsCertOptions"/> at startup, scoped by the active
/// <see cref="HostingMode"/>. IIS and Console modes require nothing. Service mode
/// requires exactly one of Thumbprint or PfxPath and rejects unsupported store locations.
/// </summary>
internal sealed class KestrelHttpsCertOptionsValidator : IValidateOptions<KestrelHttpsCertOptions>
{
    private readonly Func<HostingMode> _getMode;

    public KestrelHttpsCertOptionsValidator(Func<HostingMode> getMode)
    {
        _getMode = getMode;
    }

    public ValidateOptionsResult Validate(string? name, KestrelHttpsCertOptions options)
    {
        var mode = _getMode();

        // IIS and Console modes: nothing to validate — Kestrel:HttpsCert is Service-mode only.
        if (mode != HostingMode.Service) return ValidateOptionsResult.Success;

        var failures = new List<string>();

        var hasThumbprint = !string.IsNullOrWhiteSpace(options.Thumbprint);
        var hasPfx = !string.IsNullOrWhiteSpace(options.PfxPath);

        if (hasThumbprint && hasPfx)
        {
            failures.Add($"{nameof(KestrelHttpsCertOptions.Thumbprint)} and {nameof(KestrelHttpsCertOptions.PfxPath)} are mutually exclusive; set exactly one.");
        }
        else if (!hasThumbprint && !hasPfx)
        {
            failures.Add($"Service mode requires either {nameof(KestrelHttpsCertOptions.Thumbprint)} or {nameof(KestrelHttpsCertOptions.PfxPath)} to be set.");
        }

        // StoreLocation must be a parseable enum value and not CurrentUser (service identity non-portable).
        if (hasThumbprint && !Enum.TryParse<StoreLocation>(options.StoreLocation, ignoreCase: true, out var parsed))
        {
            failures.Add($"{nameof(KestrelHttpsCertOptions.StoreLocation)} '{options.StoreLocation}' is not a valid value. Expected LocalMachine (recommended) or one of the other System.Security.Cryptography.X509Certificates.StoreLocation values.");
        }
        else if (hasThumbprint && parsed == StoreLocation.CurrentUser)
        {
            failures.Add($"{nameof(KestrelHttpsCertOptions.StoreLocation)} CurrentUser is not supported in Service mode; use LocalMachine.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj --filter FullyQualifiedName~KestrelHttpsCertOptionsValidatorTests --configuration Release`
Expected: 8 passed, 0 failed.

- [ ] **Step 3: Commit**

```
git add src/PassReset.Web/Models/KestrelHttpsCertOptionsValidator.cs
git commit -m "feat(web): implement KestrelHttpsCertOptionsValidator [phase-14]"
```

---

### Task 8: Seed `Kestrel:HttpsCert` in appsettings files

**Files:**
- Modify: `src/PassReset.Web/appsettings.json`
- Modify: `src/PassReset.Web/appsettings.Production.template.json`

- [ ] **Step 1: Read both files**

Read both files fully so you know the exact position of the closing brace and the previous last top-level key.

- [ ] **Step 2: Add `Kestrel` block to `appsettings.json`**

Append a new top-level `Kestrel` section at the end of the root object (after the previously-last key, with a trailing comma on that key). Indentation: 2 spaces at root, 4 spaces inside, matching the existing style.

```jsonc
  "Kestrel": {
    // Service mode only — IIS ignores. Set either Thumbprint or PfxPath, not both.
    "HttpsCert": {
      "Thumbprint": null,
      "StoreLocation": "LocalMachine",
      "StoreName": "My",
      "PfxPath": null,
      "PfxPassword": null
    }
  }
```

- [ ] **Step 3: Add the same block to `appsettings.Production.template.json` — but WITHOUT JSONC comments**

The production template has a `Template_Has_Zero_Json_Comments` test (Phase 13 lesson). Strip the `//` comment line:

```jsonc
  "Kestrel": {
    "HttpsCert": {
      "Thumbprint": null,
      "StoreLocation": "LocalMachine",
      "StoreName": "My",
      "PfxPath": null,
      "PfxPassword": null
    }
  }
```

- [ ] **Step 4: Build**

Run: `dotnet build src/PassReset.Web/PassReset.Web.csproj --configuration Release`
Expected: clean.

- [ ] **Step 5: Run the template-no-comments guard test**

Run: `dotnet test src/PassReset.Tests/PassReset.Tests.csproj --filter FullyQualifiedName~SchemaArtifactTests --configuration Release`
Expected: all pass (Template_Has_Zero_Json_Comments + Template_Validates_Against_Schema_Structurally).

- [ ] **Step 6: Commit**

```
git add src/PassReset.Web/appsettings.json src/PassReset.Web/appsettings.Production.template.json
git commit -m "chore(web): seed Kestrel:HttpsCert defaults in appsettings templates [phase-14]"
```

---

### Task 9: `Program.cs` — register `KestrelHttpsCertOptions` + detector + validator

**Files:**
- Modify: `src/PassReset.Web/Program.cs`

- [ ] **Step 1: Read `Program.cs` to find anchors**

Open `src/PassReset.Web/Program.cs`. Locate:
- Existing usings block (top of file)
- `AdminSettings` registration (the `builder.Services.AddOptions<AdminSettings>()…` block added in Phase 13)

- [ ] **Step 2: Add usings**

After the existing `using PassReset.Web.Services.Configuration;` add:

```csharp
using PassReset.Web.Services.Hosting;
```

- [ ] **Step 3: Register `HostingModeDetector` as a singleton (before `AddOptions` blocks)**

Insert near the top of the service registration section (immediately before the `builder.Services.AddOptions<ClientSettings>()…` line), so options validators can depend on it:

```csharp
    // ── Phase 14: Hosting mode detection (used by KestrelHttpsCertOptionsValidator) ─
    var hostingModeDetector = new HostingModeDetector();
    var hostingMode = hostingModeDetector.Detect();
    builder.Services.AddSingleton(hostingModeDetector);
```

- [ ] **Step 4: Register `KestrelHttpsCertOptions` + validator (after the `AdminSettings` registration)**

Immediately after the `AdminSettings` options block (the one ending with `var adminSettings = builder.Configuration.GetSection(nameof(AdminSettings)).Get<AdminSettings>() ?? new AdminSettings();` from Phase 13), add:

```csharp
    builder.Services.AddOptions<KestrelHttpsCertOptions>()
        .Bind(builder.Configuration.GetSection("Kestrel:HttpsCert"))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<KestrelHttpsCertOptions>>(
        new KestrelHttpsCertOptionsValidator(() => hostingMode));

    var kestrelHttpsCert = builder.Configuration
        .GetSection("Kestrel:HttpsCert")
        .Get<KestrelHttpsCertOptions>() ?? new KestrelHttpsCertOptions();
```

- [ ] **Step 5: Build**

Run: `dotnet build src/PassReset.sln --configuration Release`
Expected: clean.

- [ ] **Step 6: Run full test suite**

Run: `dotnet test src/PassReset.sln --configuration Release --no-build`
Expected: 0 failures. The new validator is wired; all existing tests still pass.

- [ ] **Step 7: Commit**

```
git add src/PassReset.Web/Program.cs
git commit -m "feat(web): register KestrelHttpsCertOptions + HostingModeDetector in DI [phase-14]"
```

---

### Task 10: `Program.cs` — `.UseWindowsService()` + hosting-mode startup log

**Files:**
- Modify: `src/PassReset.Web/Program.cs`

- [ ] **Step 1: Read the Serilog/host configuration section**

Find the `builder.Host.UseSerilog(…)` call. The `.UseWindowsService()` call should come on `builder.Host` as well, right after Serilog setup.

- [ ] **Step 2: Add `.UseWindowsService()` + service startup configuration**

Immediately after the `builder.Host.UseSerilog(…)` block, add:

```csharp
    // ── Phase 14: Windows Service support ────────────────────────────────────────
    // When the process is launched by SCM, bind to the Windows Service lifetime so
    // Start/Stop/Restart flow through SCM. When launched from console (IIS or dev),
    // this is a no-op: WindowsServiceHelpers.IsWindowsService() returns false and
    // the host uses the default console lifetime.
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = "PassReset";
    });
```

- [ ] **Step 3: Add hosting-mode log line after `var app = builder.Build();`**

Find `var app = builder.Build();`. Immediately after it, add:

```csharp
    Log.Information(
        "PassReset starting. HostingMode: {HostingMode}. Process: {Process}. Endpoint: {Urls}.",
        hostingMode,
        System.Diagnostics.Process.GetCurrentProcess().ProcessName,
        string.Join(", ", app.Urls.DefaultIfEmpty("(not yet bound)")));
```

(`hostingMode` is the variable you declared in Task 9.)

- [ ] **Step 4: Build + full regression**

Run: `dotnet build src/PassReset.sln --configuration Release`
Expected: clean.

Run: `dotnet test src/PassReset.sln --configuration Release --no-build`
Expected: 0 failures; skip counts unchanged from baseline.

- [ ] **Step 5: Smoke-run (console mode)**

Run: `dotnet run --project src/PassReset.Web --configuration Release -- --urls=http://127.0.0.1:5099`
Expected: Serilog console output includes `HostingMode: Console`. Kill with Ctrl+C after confirming.

- [ ] **Step 6: Commit**

```
git add src/PassReset.Web/Program.cs
git commit -m "feat(web): enable Windows Service hosting + log mode at startup [phase-14]"
```

---

### Task 11: `Program.cs` — conditional Kestrel HTTPS binding from options

**Files:**
- Modify: `src/PassReset.Web/Program.cs`

- [ ] **Step 1: Read the existing Kestrel / WebHost configuration**

Find where `builder.WebHost.ConfigureKestrel(…)` is called (this exists from Phase 13 for the admin UI listener). You'll add a parallel branch that binds a Service-mode TLS endpoint on the public listener.

- [ ] **Step 2: Add a conditional public-listener HTTPS binding (Service mode only)**

Immediately before `var app = builder.Build();`, add:

```csharp
    // ── Phase 14: Service-mode TLS binding ───────────────────────────────────────
    // IIS mode: the ASP.NET Core Module feeds the request via the named-pipe backend,
    // so we don't bind a listener here. Console mode: the operator passes --urls on
    // the command line (or leaves the default). Service mode: bind HTTPS 443 with
    // the configured cert.
    if (hostingMode == HostingMode.Service)
    {
        builder.WebHost.ConfigureKestrel(opts =>
        {
            if (!string.IsNullOrWhiteSpace(kestrelHttpsCert.Thumbprint))
            {
                var storeLocation = Enum.Parse<System.Security.Cryptography.X509Certificates.StoreLocation>(
                    kestrelHttpsCert.StoreLocation, ignoreCase: true);
                using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                    kestrelHttpsCert.StoreName, storeLocation);
                store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
                var cert = store.Certificates
                    .Find(System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint,
                          kestrelHttpsCert.Thumbprint, validOnly: false)
                    .OfType<System.Security.Cryptography.X509Certificates.X509Certificate2>()
                    .FirstOrDefault()
                    ?? throw new InvalidOperationException(
                        $"Kestrel:HttpsCert.Thumbprint '{kestrelHttpsCert.Thumbprint}' not found in {kestrelHttpsCert.StoreLocation}/{kestrelHttpsCert.StoreName}.");

                opts.Listen(IPAddress.Any, 443, listen => listen.UseHttps(cert));
            }
            else if (!string.IsNullOrWhiteSpace(kestrelHttpsCert.PfxPath))
            {
                opts.Listen(IPAddress.Any, 443, listen =>
                    listen.UseHttps(kestrelHttpsCert.PfxPath!, kestrelHttpsCert.PfxPassword));
            }
        });
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build src/PassReset.sln --configuration Release`
Expected: clean (the `IPAddress` using is already in Program.cs from Phase 13).

- [ ] **Step 4: Run full test suite**

Run: `dotnet test src/PassReset.sln --configuration Release --no-build`
Expected: 0 failures. The new code path only executes when `hostingMode == Service`; under test (`WebApplicationFactory`) we're in Console mode, so this branch is dormant.

- [ ] **Step 5: Commit**

```
git add src/PassReset.Web/Program.cs
git commit -m "feat(web): Kestrel HTTPS binding from KestrelHttpsCertOptions in Service mode [phase-14]"
```

---

### Task 12: `Program.cs` — cross-mode regression verification

**Files:** none (verification only).

- [ ] **Step 1: Full backend regression**

Run: `dotnet build src/PassReset.sln --configuration Release`
Run: `dotnet test src/PassReset.sln --configuration Release --no-build`

Expected: 0 failures. Skip counts match the post-Phase-13 baseline (5 skipped in `PassReset.Tests.Windows.Admin.*` from the WAF gap, 1 skipped Samba integration).

- [ ] **Step 2: Frontend regression**

Run: `cd src/PassReset.Web/ClientApp && npm test -- --run`
Expected: 54/54 passed.

- [ ] **Step 3: Console smoke-run sanity check**

Run: `dotnet run --project src/PassReset.Web --configuration Release -- --urls=http://127.0.0.1:5099`

Verify: Serilog log line contains `HostingMode: Console`. `curl http://127.0.0.1:5099/api/health` returns 200. Kill the process.

- [ ] **Step 4: No commit — verification gate only**

If everything passes, proceed to Task 13. If any regression appears, STOP and report.

---

### Task 13: Installer Pester test-file scaffold

**Files:**
- Create: `deploy/Install-PassReset.Tests.ps1`

- [ ] **Step 1: Verify Pester is available**

Run: `pwsh -NoProfile -Command "Get-Module -ListAvailable Pester | Select-Object Version"`
Expected: a Pester version ≥ 5.3.0.

If Pester is unavailable or older: `pwsh -NoProfile -Command "Install-Module Pester -MinimumVersion 5.3.0 -Scope CurrentUser -Force -SkipPublisherCheck"`.

- [ ] **Step 2: Create the Pester scaffold**

Write `deploy/Install-PassReset.Tests.ps1`:

```powershell
# Pester tests for Install-PassReset.ps1.
# Run: pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"
#
# These tests exercise the installer in "dry-run" / function-extraction mode. We
# dot-source the installer to load its functions without executing its top-level
# install flow. The installer's top-level script block checks for the
# $PASSRESET_TEST_MODE env var and short-circuits when set.

BeforeAll {
    $env:PASSRESET_TEST_MODE = '1'
    . "$PSScriptRoot/Install-PassReset.ps1"
}

AfterAll {
    Remove-Item Env:PASSRESET_TEST_MODE -ErrorAction SilentlyContinue
}

Describe 'Install-PassReset: -HostingMode param' {
    It 'accepts IIS' {
        { Test-HostingModeValue -HostingMode 'IIS' } | Should -Not -Throw
    }
    It 'accepts Service' {
        { Test-HostingModeValue -HostingMode 'Service' } | Should -Not -Throw
    }
    It 'accepts Console' {
        { Test-HostingModeValue -HostingMode 'Console' } | Should -Not -Throw
    }
    It 'rejects unknown values' {
        { Test-HostingModeValue -HostingMode 'Nonsense' } | Should -Throw
    }
}

Describe 'Install-PassReset: Test-ServiceModePreflight' {
    It 'returns $false when cert thumbprint is empty' {
        Test-ServiceModePreflight -CertThumbprint '' -Port 443 -ServiceAccount 'NT SERVICE\PassReset' |
            Should -BeFalse
    }
    It 'returns $false when Port is already bound' {
        # Bind a TCP listener on a free high port, then assert preflight fails.
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        try {
            $port = ($listener.LocalEndpoint).Port
            Test-ServiceModePreflight -CertThumbprint 'ABCDEF' -Port $port -ServiceAccount 'NT SERVICE\PassReset' |
                Should -BeFalse
        } finally {
            $listener.Stop()
        }
    }
}
```

**Why the helpers exist:** Tasks 14-17 introduce `Test-HostingModeValue` and `Test-ServiceModePreflight` as named functions inside `Install-PassReset.ps1`, plus the `PASSRESET_TEST_MODE` short-circuit. The Pester file is intentionally written before those functions exist so the red/green discipline is visible.

- [ ] **Step 3: Run Pester to verify red**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"`
Expected: failures — the installer functions don't exist yet, and `PASSRESET_TEST_MODE` short-circuit isn't in place.

- [ ] **Step 4: Commit**

```
git add deploy/Install-PassReset.Tests.ps1
git commit -m "test(installer): Pester scaffold for Install-PassReset (failing) [phase-14]"
```

---

### Task 14: Installer `-HostingMode` param + prompt + test-mode short-circuit

**Files:**
- Modify: `deploy/Install-PassReset.ps1`

- [ ] **Step 1: Read the top of `Install-PassReset.ps1`**

Locate the `param(...)` block.

- [ ] **Step 2: Add `-HostingMode` parameter**

Inside `param(...)`, add (keeping the existing params intact; comma-separate as needed):

```powershell
    [ValidateSet('IIS','Service','Console')]
    [string] $HostingMode,
```

- [ ] **Step 3: Add `Test-HostingModeValue` helper (dot-sourceable)**

After the `param(...)` block but before any top-level execution, add:

```powershell
function Test-HostingModeValue {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $HostingMode)
    # Uses PowerShell's ValidateSet attribute indirectly via a stub with the same set.
    $valid = @('IIS','Service','Console')
    if ($valid -notcontains $HostingMode) {
        throw "HostingMode '$HostingMode' is not valid. Expected one of: $($valid -join ', ')."
    }
    return $true
}
```

- [ ] **Step 4: Add `Get-HostingModeInteractive` helper for fresh-install prompting**

After `Test-HostingModeValue`:

```powershell
function Get-HostingModeInteractive {
    [CmdletBinding()]
    param(
        [Parameter()] [string] $Default  # 'IIS' on upgrade of IIS-hosted install; $null on fresh
    )
    $prompt = if ($Default) {
        "Hosting mode? [I]IS / [S]ervice / [C]onsole (default: $Default)"
    } else {
        "Hosting mode? [I]IS / [S]ervice / [C]onsole"
    }
    while ($true) {
        $input = Read-Host $prompt
        if (-not $input -and $Default) { return $Default }
        switch -Regex ($input) {
            '^[Ii]' { return 'IIS' }
            '^[Ss]' { return 'Service' }
            '^[Cc]' { return 'Console' }
            default { Write-Host "Please answer I, S, or C." -ForegroundColor Yellow }
        }
    }
}
```

- [ ] **Step 5: Add the test-mode short-circuit**

Directly after the helper-function block (before any top-level execution that does IIS/Service work), add:

```powershell
# Pester test mode: dot-source the script to import functions without executing the install flow.
if ($env:PASSRESET_TEST_MODE -eq '1') {
    return
}
```

- [ ] **Step 6: Run Pester — the hosting-mode tests should now pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"`
Expected: the four `-HostingMode param` tests pass; the `Test-ServiceModePreflight` tests still fail (function not yet defined).

- [ ] **Step 7: Commit**

```
git add deploy/Install-PassReset.ps1
git commit -m "feat(installer): -HostingMode param + test-mode dot-source [phase-14]"
```

---

### Task 15: Installer preflight — cert resolution helper

**Files:**
- Modify: `deploy/Install-PassReset.ps1`

- [ ] **Step 1: Add `Resolve-HttpsCertificate` helper**

After `Get-HostingModeInteractive`, before the test-mode short-circuit, add:

```powershell
function Resolve-HttpsCertificate {
    <#
    .SYNOPSIS
    Resolves a cert in Service mode: either a thumbprint in LocalMachine\My or a PFX file path.
    Returns $null if neither is usable.
    #>
    [CmdletBinding()]
    param(
        [Parameter()] [string] $Thumbprint,
        [Parameter()] [string] $PfxPath,
        [Parameter()] [securestring] $PfxPassword
    )

    if ($Thumbprint) {
        $cert = Get-ChildItem -Path "Cert:\LocalMachine\My" |
            Where-Object Thumbprint -eq ($Thumbprint -replace '\s','').ToUpperInvariant() |
            Select-Object -First 1
        if (-not $cert) {
            Write-Warning "Certificate with thumbprint '$Thumbprint' not found in Cert:\LocalMachine\My."
            return $null
        }
        if ($cert.NotAfter -lt (Get-Date)) {
            Write-Warning "Certificate '$($cert.Subject)' expired on $($cert.NotAfter)."
            return $null
        }
        return $cert
    }

    if ($PfxPath) {
        if (-not (Test-Path $PfxPath)) {
            Write-Warning "PFX file '$PfxPath' does not exist."
            return $null
        }
        try {
            $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, $PfxPassword)
            return $cert
        } catch {
            Write-Warning "Could not open PFX at '$PfxPath': $_"
            return $null
        }
    }

    return $null
}
```

- [ ] **Step 2: Commit**

```
git add deploy/Install-PassReset.ps1
git commit -m "feat(installer): Resolve-HttpsCertificate helper [phase-14]"
```

---

### Task 16: Installer preflight — port availability + orchestrator

**Files:**
- Modify: `deploy/Install-PassReset.ps1`

- [ ] **Step 1: Add `Test-PortFree` helper**

Immediately after `Resolve-HttpsCertificate`:

```powershell
function Test-PortFree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [int] $Port,
        [Parameter()] [string] $OwnedByIisSite  # if a site is bound to this port and matches, treat as free (will be torn down)
    )

    $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if (-not $conn) { return $true }

    # Hook point for IIS-site-owned bindings during migration.
    if ($OwnedByIisSite) {
        $iisBinding = Get-Website -Name $OwnedByIisSite -ErrorAction SilentlyContinue
        if ($iisBinding -and ($iisBinding.Bindings.Collection.bindingInformation -match ":$Port:")) {
            Write-Verbose "Port $Port is bound by IIS site '$OwnedByIisSite' — will be freed at teardown."
            return $true
        }
    }

    $pid = $conn[0].OwningProcess
    $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
    Write-Warning "Port $Port is bound by process $pid ($($proc.ProcessName))."
    return $false
}
```

- [ ] **Step 2: Add `Test-ServiceModePreflight` orchestrator**

Immediately after `Test-PortFree`:

```powershell
function Test-ServiceModePreflight {
    <#
    .SYNOPSIS
    Runs all Service-mode preconditions. Returns $true only if every check passes.
    On failure, writes a clear Write-Warning for each failed check and returns $false.
    Does NOT touch IIS or create services.
    #>
    [CmdletBinding()]
    param(
        [Parameter()] [string] $CertThumbprint,
        [Parameter()] [string] $PfxPath,
        [Parameter()] [securestring] $PfxPassword,
        [Parameter()] [int] $Port = 443,
        [Parameter()] [string] $ServiceAccount = 'NT SERVICE\PassReset',
        [Parameter()] [string] $MigrateFromIisSite  # optional: existing site name being torn down
    )

    $ok = $true

    if (-not (Resolve-HttpsCertificate -Thumbprint $CertThumbprint -PfxPath $PfxPath -PfxPassword $PfxPassword)) {
        Write-Warning "Cert preflight failed."
        $ok = $false
    }

    if (-not (Test-PortFree -Port $Port -OwnedByIisSite $MigrateFromIisSite)) {
        Write-Warning "Port $Port preflight failed."
        $ok = $false
    }

    # Service account: virtual accounts are always valid; domain accounts we trust the installer's
    # caller to pass correctly. We could do an LDAP probe here but won't in this phase.
    if (-not $ServiceAccount) {
        Write-Warning "ServiceAccount preflight failed (empty)."
        $ok = $false
    }

    return $ok
}
```

- [ ] **Step 3: Run Pester**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"`
Expected: all Pester tests pass (hosting-mode + preflight).

- [ ] **Step 4: Commit**

```
git add deploy/Install-PassReset.ps1
git commit -m "feat(installer): Service-mode preflight (cert + port + account) [phase-14]"
```

---

### Task 17: Installer — Service registration branch

**Files:**
- Modify: `deploy/Install-PassReset.ps1`

- [ ] **Step 1: Add `Install-AsWindowsService` function**

After `Test-ServiceModePreflight`:

```powershell
function Install-AsWindowsService {
    <#
    .SYNOPSIS
    Registers PassReset as a Windows Service. Assumes files are already copied to $BinaryPath
    and preflight has passed.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $BinaryPath,    # e.g. "C:\Program Files\PassReset\PassReset.Web.exe"
        [Parameter()] [string] $ServiceAccount = 'NT SERVICE\PassReset',
        [Parameter()] [securestring] $ServicePassword,  # domain-account installs only
        [Parameter()] [string] $ServiceName = 'PassReset',
        [Parameter()] [string] $DisplayName = 'PassReset Password Reset Portal',
        [Parameter()] [string] $Description = 'Self-service Active Directory password reset portal.'
    )

    # Stop + remove an existing service with the same name (idempotent).
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -eq 'Running') { Stop-Service -Name $ServiceName -Force }
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    $newServiceArgs = @{
        Name           = $ServiceName
        BinaryPathName = "`"$BinaryPath`""
        DisplayName    = $DisplayName
        Description    = $Description
        StartupType    = 'AutomaticDelayedStart'
    }
    if ($ServicePassword) {
        $newServiceArgs.Credential = [pscredential]::new($ServiceAccount, $ServicePassword)
    }
    # Virtual accounts (NT SERVICE\*) are created by SCM without a password.

    New-Service @newServiceArgs | Out-Null
    Write-Host "Service '$ServiceName' registered. Startup: AutomaticDelayedStart. Identity: $ServiceAccount." -ForegroundColor Green

    Start-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' started." -ForegroundColor Green
}
```

- [ ] **Step 2: Wire Service mode into the top-level install flow**

Find the section of `Install-PassReset.ps1` that currently does IIS provisioning (search for `New-WebAppPool` or `New-Website` or the `# ─── IIS` section). That section should be inside an `if ($HostingMode -eq 'IIS') { ... }` block. Wrap it accordingly.

Immediately before the existing IIS provisioning section, add (pseudo-structure — adjust to match the actual top-level script layout):

```powershell
# Resolve hosting mode (prompt on fresh install, default on upgrade).
if (-not $HostingMode) {
    $existingSite = Get-Website -Name 'PassReset' -ErrorAction SilentlyContinue
    $default = if ($existingSite) { 'IIS' } else { $null }
    $HostingMode = Get-HostingModeInteractive -Default $default
}

switch ($HostingMode) {
    'IIS' {
        # (existing IIS provisioning block)
    }
    'Service' {
        if (-not (Test-ServiceModePreflight -CertThumbprint $CertThumbprint -PfxPath $PfxPath -PfxPassword $PfxPassword -ServiceAccount $ServiceAccount)) {
            throw "Service-mode preflight failed. See warnings above. No changes made."
        }
        # If migrating from IIS, tear down the existing site/AppPool (reuse existing teardown helper).
        $existingSite = Get-Website -Name 'PassReset' -ErrorAction SilentlyContinue
        if ($existingSite) {
            Write-Host "Migrating from IIS: tearing down existing site..." -ForegroundColor Yellow
            # Call existing teardown function — if one doesn't exist, Stop-Website + Remove-Website + Remove-WebAppPool inline.
            Stop-Website -Name 'PassReset' -ErrorAction SilentlyContinue
            Remove-Website -Name 'PassReset' -ErrorAction SilentlyContinue
            if (Get-IISAppPool -Name 'PassResetPool' -ErrorAction SilentlyContinue) {
                Remove-WebAppPool -Name 'PassResetPool'
            }
        }
        Install-AsWindowsService -BinaryPath (Join-Path $PhysicalPath 'PassReset.Web.exe') -ServiceAccount $ServiceAccount -ServicePassword $ServicePassword
    }
    'Console' {
        Write-Host "Console mode: files copied to $PhysicalPath. Run 'dotnet $PhysicalPath\PassReset.Web.dll' to start the app manually." -ForegroundColor Cyan
    }
}
```

**Implementer note:** The exact placement depends on the existing `Install-PassReset.ps1` structure (there's a substantial script ~1100+ lines). Wrap the current IIS provisioning block in `if ($HostingMode -eq 'IIS') {}` and place the Service / Console branches before or after as appropriate. Do NOT duplicate the IIS logic — move it into the switch.

- [ ] **Step 3: Re-run Pester (must not regress)**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"`
Expected: all tests still pass.

- [ ] **Step 4: Syntax check**

Run:

```powershell
pwsh -NoProfile -Command "`$null = [System.Management.Automation.PSParser]::Tokenize((Get-Content deploy/Install-PassReset.ps1 -Raw), [ref]`$null); 'syntax ok'"
```

Expected: `syntax ok`.

- [ ] **Step 5: Commit**

```
git add deploy/Install-PassReset.ps1
git commit -m "feat(installer): Service-mode registration branch + IIS migration teardown [phase-14]"
```

---

### Task 18: Installer — `-ServiceAccount` + `-PfxPath/-PfxPassword` params

**Files:**
- Modify: `deploy/Install-PassReset.ps1`

- [ ] **Step 1: Add parameters to the `param(...)` block**

Inside `param(...)`:

```powershell
    # Service mode: identity
    [string] $ServiceAccount = 'NT SERVICE\PassReset',
    [securestring] $ServicePassword,

    # Service mode: cert alternative to -CertThumbprint (mutually exclusive)
    [string] $PfxPath,
    [securestring] $PfxPassword,
```

- [ ] **Step 2: Add mutual-exclusion check near the top of the top-level flow**

After the `$HostingMode` resolution (Task 17), add:

```powershell
if ($HostingMode -eq 'Service') {
    if ($CertThumbprint -and $PfxPath) {
        throw "Specify either -CertThumbprint or -PfxPath, not both."
    }
    if (-not $CertThumbprint -and -not $PfxPath) {
        throw "Service mode requires -CertThumbprint or -PfxPath."
    }
}
```

- [ ] **Step 3: Syntax + Pester**

Run syntax check from Task 17 Step 4.
Run Pester. Expected: all pass.

- [ ] **Step 4: Commit**

```
git add deploy/Install-PassReset.ps1
git commit -m "feat(installer): -ServiceAccount + -PfxPath/-PfxPassword params [phase-14]"
```

---

### Task 19: Uninstall-PassReset.ps1 — Service branch

**Files:**
- Modify: `deploy/Uninstall-PassReset.ps1`

- [ ] **Step 1: Read the existing uninstaller**

Locate the existing IIS teardown section.

- [ ] **Step 2: Add Service detection + removal**

Before the existing IIS teardown, add:

```powershell
# Detect hosting mode by looking for either an IIS site or a Windows Service named 'PassReset'.
$svc = Get-Service -Name 'PassReset' -ErrorAction SilentlyContinue
$site = Get-Website -Name 'PassReset' -ErrorAction SilentlyContinue

if ($svc -and $site) {
    Write-Warning "Both a Windows Service and an IIS site named 'PassReset' exist. Removing both."
}

if ($svc) {
    Write-Host "Stopping service 'PassReset'..." -ForegroundColor Cyan
    if ($svc.Status -eq 'Running') { Stop-Service -Name 'PassReset' -Force }
    Write-Host "Removing service 'PassReset'..." -ForegroundColor Cyan
    sc.exe delete 'PassReset' | Out-Null
    Start-Sleep -Seconds 2
}
```

Keep the existing IIS teardown block intact — it runs only if the IIS site exists.

- [ ] **Step 3: Syntax check**

Same command as Task 17 Step 4, but for `Uninstall-PassReset.ps1`.
Expected: `syntax ok`.

- [ ] **Step 4: Commit**

```
git add deploy/Uninstall-PassReset.ps1
git commit -m "feat(installer): Uninstall-PassReset handles Service mode [phase-14]"
```

---

### Task 20: Admin UI + IIS verification (smoke task)

**Files:** none — manual verification only. Records the outcome in `docs/Deployment.md` later (Task 21).

- [ ] **Step 1: Attempt admin UI + IIS smoke**

If a test IIS host is available:
1. Install the current branch under IIS (using existing `-HostingMode IIS`).
2. Set `AdminSettings.Enabled=true` + `LoopbackPort=5010` in `appsettings.Production.json`.
3. Recycle the app pool.
4. RDP to the box, browse `http://localhost:5010/admin`.

Possible outcomes:
- **A) Works:** admin UI renders on the secondary loopback Kestrel listener even while IIS fronts the main listener. Document in Task 21's `docs/Deployment.md` as "Admin UI works in all three hosting modes."
- **B) Doesn't work:** log the exact failure (404? connection refused? startup crash?). Document in Task 21 as "Admin UI unavailable in IIS mode — use Service mode to access the admin UI." In that case, also gate admin UI startup in `Program.cs` to only register the secondary listener when `hostingMode != HostingMode.Iis`.

- [ ] **Step 2: If outcome is (B), make the code change**

Find the Phase 13 admin-UI listener configuration in `Program.cs`:

```csharp
if (adminSettings.Enabled)
{
    builder.WebHost.ConfigureKestrel(opts =>
    {
        opts.Listen(IPAddress.Loopback, adminSettings.LoopbackPort);
    });
}
```

Change to:

```csharp
if (adminSettings.Enabled && hostingMode != HostingMode.Iis)
{
    builder.WebHost.ConfigureKestrel(opts =>
    {
        opts.Listen(IPAddress.Loopback, adminSettings.LoopbackPort);
    });
}
```

And add a matching guard on the admin routing block:

```csharp
if (adminSettings.Enabled && hostingMode != HostingMode.Iis)
{
    app.MapWhen(
        ctx => ctx.Request.Path.StartsWithSegments("/admin"),
        admin => { … });
}
```

- [ ] **Step 3: If outcome is (A), skip Step 2 and just note it for Task 21**

- [ ] **Step 4: If any code change was made, build + regression**

Run: `dotnet build src/PassReset.sln --configuration Release`
Run: `dotnet test src/PassReset.sln --configuration Release --no-build`
Expected: 0 failures.

- [ ] **Step 5: If code changed, commit**

```
git add src/PassReset.Web/Program.cs
git commit -m "fix(web): disable admin UI listener under IIS [phase-14]"
```

If no code changed, skip the commit and proceed.

---

### Task 21: `docs/Deployment.md` — consolidated guide

**Files:**
- Create: `docs/Deployment.md`

- [ ] **Step 1: Write `docs/Deployment.md`**

Create the file with the following structure (content provided verbatim; fill in the admin-UI outcome from Task 20):

````markdown
# Deployment

PassReset v2.0 supports three hosting modes on Windows Server. Pick the mode at install time via `Install-PassReset.ps1 -HostingMode IIS|Service|Console`. Fresh installs prompt if the flag is omitted; upgrades default to the currently-installed mode.

## Quick comparison

| Mode | TLS termination | Service manager | Typical use case |
|---|---|---|---|
| **IIS** | IIS | IIS AppPool lifecycle | Existing IIS-standardized infrastructure |
| **Windows Service** | Kestrel (direct) | Windows Service Control Manager | New installs wanting no IIS dependency |
| **Console** | Kestrel (optional) | operator runs the binary manually | Development, debugging |

## IIS mode {#iis-mode}

Covers the classic v1.x deployment. IIS fronts a Kestrel backend via the ASP.NET Core Module; IIS terminates TLS using a cert bound to its HTTPS binding.

### Install

```powershell
.\Install-PassReset.ps1 -HostingMode IIS -CertThumbprint ABCDEF0123456789...
```

### Upgrade

```powershell
.\Install-PassReset.ps1 -Force
# Installer detects existing IIS site, defaults to IIS on upgrade.
```

### Migrate to Service mode

See [Migrating IIS to Service mode](#migrating-iis-to-service-mode).

## Windows Service mode {#service-mode}

Kestrel handles everything, including TLS. No IIS, no reverse proxy required.

### Install

```powershell
# Option A: cert from LocalMachine\My by thumbprint
.\Install-PassReset.ps1 -HostingMode Service -CertThumbprint ABCDEF0123456789...

# Option B: PFX file
.\Install-PassReset.ps1 -HostingMode Service -PfxPath 'C:\certs\passreset.pfx' -PfxPassword (ConvertTo-SecureString -AsPlainText -Force 'MyPfxPass')
```

### Service identity

- Default: `NT SERVICE\PassReset` — a virtual service account, no password management.
- Override: `-ServiceAccount DOMAIN\user -ServicePassword (Read-Host -AsSecureString)`.

### Startup type

`Automatic (Delayed)` — the service starts ~2 minutes after boot, after the network and AD are available. This avoids first-connect failures when the server reboots before the domain controller is reachable.

### Cert configuration (`appsettings.Production.json`)

The installer writes these values based on what you passed:

```json
"Kestrel": {
  "HttpsCert": {
    "Thumbprint": "ABCDEF0123456789...",
    "StoreLocation": "LocalMachine",
    "StoreName": "My",
    "PfxPath": null,
    "PfxPassword": null
  }
}
```

To rotate the cert without reinstalling: edit `appsettings.Production.json`, run `Restart-Service PassReset`.

### Managing the service

```powershell
Get-Service PassReset                # status
Start-Service PassReset              # start
Stop-Service PassReset -Force        # stop
Restart-Service PassReset            # apply config changes
```

Or use `services.msc`.

### Admin UI (Phase 13)

*(Fill in one of these based on Task 20 outcome.)*

**If Task 20 Step 1 outcome was A (works everywhere):**
> The loopback admin UI works in all three hosting modes. Set `AdminSettings.Enabled=true` and restart the service. Browse `http://localhost:5010/admin` from the server console.

**If Task 20 outcome was B (IIS blocks it):**
> The admin UI is available in Service and Console modes but NOT in IIS mode (the ASP.NET Core Module's hosting model prevents a secondary Kestrel listener). To use the admin UI on an IIS-hosted install, either switch to Service mode or edit `appsettings.Production.json` directly.

## Console mode {#console-mode}

Dev / debugging. The installer copies files but doesn't register a service or IIS site.

```powershell
.\Install-PassReset.ps1 -HostingMode Console
# Then to run:
dotnet "C:\Program Files\PassReset\PassReset.Web.dll" --urls=https://localhost:5001
```

## Migrating IIS to Service mode {#migrating-iis-to-service-mode}

```powershell
.\Install-PassReset.ps1 -Force -HostingMode Service -CertThumbprint ABCDEF...
```

The installer runs a **preflight dry-run** before touching IIS:
1. Certificate exists in `LocalMachine\My` (or PFX decrypts).
2. Port 443 is available, or the binding on port 443 is owned by the PassReset IIS site (which will be torn down).
3. Service account is valid.

If any preflight step fails, the installer aborts with a clear error and IIS is untouched. Re-run after fixing the failure, or revert to IIS via `.\Install-PassReset.ps1 -HostingMode IIS`.

## Troubleshooting

**Service fails to start:** Check Windows Event Viewer → Windows Logs → System for SCM events (7000-series). Application-layer failures (config validation, cert load) are logged to `Application` under source `PassReset`.

**Cert not found:** Verify `LocalMachine\My` contains the thumbprint: `Get-ChildItem Cert:\LocalMachine\My | Where-Object Thumbprint -eq 'ABCDEF...'`.

**Port 443 already bound:** `Get-NetTCPConnection -LocalPort 443 -State Listen` reveals the owner. Stop that process or pick another port via `Kestrel:Endpoints` in `appsettings.Production.json`.

**Virtual service account can't decrypt `secrets.dat`:** Phase 13's Data Protection keys are DPAPI-scoped to the identity that created them. If migrating from IIS (where the AppPool identity created the keys) to a virtual service account, the new account can't decrypt. Remedy: re-enter secrets via the admin UI after migration, OR set `AdminSettings.KeyStorePath` to a shared directory and use machine-scope DPAPI (documented at `docs/Admin-UI.md`).
````

- [ ] **Step 2: Commit**

```
git add docs/Deployment.md
git commit -m "docs: consolidated Deployment.md (IIS/Service/Console) [phase-14]"
```

---

### Task 22: Collateral docs — `IIS-Setup.md` redirect + `appsettings-Production.md` + `CLAUDE.md`

**Files:**
- Modify: `docs/IIS-Setup.md`
- Modify: `docs/appsettings-Production.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Reduce `docs/IIS-Setup.md` to a redirect stub**

Read the file. Replace its entire content with:

```markdown
# IIS Setup

IIS is one of three hosting modes in v2.0. For the full guide covering IIS, Windows Service, and Console modes, see **[docs/Deployment.md](Deployment.md)**.

The content that previously lived here has been consolidated into that document under the **[IIS mode](Deployment.md#iis-mode)** section.
```

- [ ] **Step 2: Add `Kestrel:HttpsCert` section to `docs/appsettings-Production.md`**

Read `docs/appsettings-Production.md` to find the right insertion point (after the existing `AdminSettings` section added in Phase 13). Add:

```markdown
### `Kestrel:HttpsCert`

Service-mode only. Ignored under IIS (IIS terminates TLS) and Console mode (operator passes `--urls`). Set exactly one of `Thumbprint` or `PfxPath`.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Thumbprint` | string or null | `null` | SHA-1 thumbprint of a cert in `StoreLocation\StoreName`. |
| `StoreLocation` | string | `"LocalMachine"` | Cert store location. `CurrentUser` is not supported in Service mode. |
| `StoreName` | string | `"My"` | Cert store name. |
| `PfxPath` | string or null | `null` | Absolute path to a PFX file. Mutually exclusive with `Thumbprint`. |
| `PfxPassword` | string or null | `null` | Password for the PFX file. Store via `secrets.dat` (Phase 13) in production. |

See [docs/Deployment.md#service-mode](Deployment.md#service-mode) for the operator workflow.
```

- [ ] **Step 3: Update `CLAUDE.md`**

Read the project `CLAUDE.md`. Find the "Configuration keys to know" section (it contains entries for `PasswordChangeOptions`, `ClientSettings`, `SiemSettings`, `PasswordExpiryNotificationSettings`, `AdminSettings`). Append:

```markdown
`KestrelHttpsCertOptions` (server-only, Phase 14):
- `Thumbprint` — SHA-1 thumbprint in `LocalMachine\My`
- `StoreLocation` / `StoreName` — override cert store (default `LocalMachine\My`)
- `PfxPath` / `PfxPassword` — file-based alternative to store lookup
- Used only when `HostingMode == Service`. IIS and Console modes ignore.
- See `docs/Deployment.md` for the operator workflow.
```

- [ ] **Step 4: Commit**

```
git add docs/IIS-Setup.md docs/appsettings-Production.md CLAUDE.md
git commit -m "docs: Kestrel:HttpsCert reference + IIS-Setup redirect stub [phase-14]"
```

---

### Task 23: `CHANGELOG.md` entry

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Read the current `[Unreleased]` section**

The section has `### Added`, `### Configuration`, `### Security`, `### Non-changes` subsections (from Phase 11-13). Merge Phase 14 bullets into each (do not duplicate headers).

- [ ] **Step 2: Append to `### Added`**

Add under the existing Phase 13 Admin UI bullet:

```markdown
- **Hosting modes** ([V2-004]): Windows Service and Console are now supported alongside
  IIS. Operators pick at install time via `Install-PassReset.ps1 -HostingMode
  IIS|Service|Console`. Windows Service mode runs the app under SCM with
  Kestrel-terminated TLS (cert by thumbprint or PFX). Existing IIS installs stay on
  IIS on upgrade unless the operator explicitly migrates. See `docs/Deployment.md`.
  *(web, installer, docs)*
```

- [ ] **Step 3: Append to `### Configuration`**

```markdown
- `Kestrel:HttpsCert.Thumbprint` / `StoreLocation` / `StoreName` — cert store lookup (Service mode)
- `Kestrel:HttpsCert.PfxPath` / `PfxPassword` — PFX file alternative (Service mode)
```

- [ ] **Step 4: Append to `### Security`**

```markdown
- `Kestrel:HttpsCert` validator rejects `CurrentUser` store location in Service mode
  (service-identity non-portable). Mutually exclusive `Thumbprint` vs `PfxPath`
  enforced at startup.
- Installer preflight dry-run verifies cert + port + service account BEFORE tearing
  down an existing IIS site, so a failed migration never leaves the operator without
  a working install.
```

- [ ] **Step 5: Commit**

```
git add CHANGELOG.md
git commit -m "docs(changelog): add Phase 14 hosting modes entry [phase-14]"
```

---

### Task 24: Full regression

**Files:** none — verification only.

- [ ] **Step 1: Backend build + tests**

```
dotnet build src/PassReset.sln --configuration Release
dotnet test  src/PassReset.sln --configuration Release --no-build
```

Expected: 0 failures. Test count roughly: baseline 307 passing + 4 new `HostingModeDetectorTests` + 8 new `KestrelHttpsCertOptionsValidatorTests` = ~319 passing. Same 6 skipped (5 WAF admin + 1 Samba).

- [ ] **Step 2: Frontend tests**

```
cd src/PassReset.Web/ClientApp && npm test -- --run
```

Expected: 54/54 passing (no frontend changes).

- [ ] **Step 3: Pester installer tests**

```
pwsh -NoProfile -Command "Invoke-Pester -Path deploy/Install-PassReset.Tests.ps1 -Output Detailed"
```

Expected: all pass.

- [ ] **Step 4: Console smoke**

```
dotnet run --project src/PassReset.Web --configuration Release -- --urls=http://127.0.0.1:5099
```

Verify Serilog line: `HostingMode: Console`. `curl http://127.0.0.1:5099/api/health` returns 200. Kill the process.

- [ ] **Step 5: Commit sanity**

```
git log --oneline master..HEAD
```

Expected: ~23 commits, one per task (plus review-loop fixups from subagent-driven-development).

- [ ] **Step 6: No commit — verification task only**

Proceed to `superpowers:finishing-a-development-branch`.

---

## Self-Review

- **Spec coverage:** Every spec section (goal, non-goals, architecture, installer UX, Windows Service details, TLS cert, admin UI, observability, upgrade paths, file structure, tests, risks, success criteria) maps to at least one task. Admin UI + IIS risk → Task 20. DPAPI / virtual-account risk → covered by the `docs/Deployment.md` troubleshooting note (Task 21) and deferred operator-verification step (no automated test is feasible in this phase). Success criterion #2 (IIS regression) → Task 12 + Task 24. Success criterion #4 (migration IIS → Service) → Tasks 15-18 functionally + Task 24 manual smoke option. Ship-ready.

- **Placeholder scan:** Two intentional branch points ("fill in based on Task 20 outcome" in Task 21, "adjust to match actual top-level script layout" in Task 17 Step 2). Both are guided — explicit options + the branching rule. Acceptable.

- **Type consistency:** `HostingMode` enum values (`Iis`, `Service`, `Console`) used consistently across Tasks 2-11. `KestrelHttpsCertOptions` property names (`Thumbprint`, `StoreLocation`, `StoreName`, `PfxPath`, `PfxPassword`) consistent across Tasks 5-11 and docs (Task 21, 22). `Test-ServiceModePreflight` / `Resolve-HttpsCertificate` / `Install-AsWindowsService` function names match between introduction (Task 15-17) and use (Task 17 top-level flow). Consistent.

- **Task dependencies:** Each task's preconditions are satisfied by earlier tasks. Task 4 depends on Task 2+3. Task 7 depends on Task 5+6. Task 9-11 depend on Tasks 1-7. Task 14 depends on Task 13 (Pester scaffold must exist for red/green). Task 17 depends on 14-16. Task 24 is terminal.

No issues found. Plan is complete.

---

*End of Phase 14 implementation plan.*
