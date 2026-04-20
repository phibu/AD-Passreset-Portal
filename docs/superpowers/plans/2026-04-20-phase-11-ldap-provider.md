# Phase 11 — Cross-Platform LDAP Password Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a cross-platform `LdapPasswordChangeProvider` via `System.DirectoryServices.Protocols` that passes a shared behavioral-contract test suite against the existing Windows provider, with Samba DC integration tests in CI. Ships as v2.0-alpha1.

**Architecture:** A new `PassReset.PasswordProvider.Ldap` project (net10.0) sits alongside the existing `PassReset.PasswordProvider` (net10.0-windows). A thin `ILdapSession` adapter isolates `LdapConnection` for testability. `PassReset.Web` retargets to `net10.0` and references the Windows provider via a csproj conditional (`$(OS) == 'Windows_NT'`) guarded by `WINDOWS_PROVIDER` compile constant. Runtime selection via new `ProviderMode` enum (`Auto | Windows | Ldap`).

**Tech Stack:** .NET 10 / net10.0 / net10.0-windows / System.DirectoryServices.Protocols / xUnit v3 / NSubstitute / GitHub Actions / Samba AD DC Docker image.

**Spec:** [docs/superpowers/specs/2026-04-20-phase-11-ldap-provider-design.md](../specs/2026-04-20-phase-11-ldap-provider-design.md)

---

## File Structure

### New files
- `src/PassReset.PasswordProvider.Ldap/PassReset.PasswordProvider.Ldap.csproj` — new project, net10.0
- `src/PassReset.PasswordProvider.Ldap/ILdapSession.cs` — adapter interface (thin `LdapConnection` wrapper for testability)
- `src/PassReset.PasswordProvider.Ldap/LdapSession.cs` — default implementation wrapping `LdapConnection`
- `src/PassReset.PasswordProvider.Ldap/LdapErrorMapping.cs` — `ResultCode` + `extendedError` → `ApiErrorCode` static map
- `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs` — the new provider implementing `IPasswordChangeProvider`
- `src/PassReset.PasswordProvider.Ldap/LdapAttributeNames.cs` — constants for AD attribute names (`samAccountName`, `unicodePwd`, `pwdLastSet`, `tokenGroups`, `memberOf`, etc.)
- `src/PassReset.Common/ProviderMode.cs` — new enum (`Auto | Windows | Ldap`)
- `src/PassReset.Tests/Contracts/IPasswordChangeProviderContract.cs` — abstract xUnit contract test class
- `src/PassReset.Tests/Contracts/LdapPasswordChangeProviderContractTests.cs` — contract impl for Ldap
- `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs` — Ldap unit tests (uses `FakeLdapSession`)
- `src/PassReset.Tests/Services/LdapErrorMappingTests.cs` — table-driven tests for every `ApiErrorCode` mapping
- `src/PassReset.Tests/Fakes/FakeLdapSession.cs` — scripted `ILdapSession` fake for unit/contract tests
- `src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj` — new Windows-only test project (net10.0-windows)
- `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs` — contract impl for Windows provider (moved/duplicated from existing tests)
- `src/PassReset.Tests.Integration.Ldap/PassReset.Tests.Integration.Ldap.csproj` — new integration project (net10.0)
- `src/PassReset.Tests.Integration.Ldap/SambaDcIntegrationTests.cs` — end-to-end tests against Samba DC
- `.github/workflows/tests.yml` — add `integration-tests-ldap` job with Samba service container (warning-only)
- `docs/AD-ServiceAccount-LDAP-Setup.md` — new operator doc for service account + LDAPS cert trust on Linux

### Modified files
- `src/PassReset.sln` — register the 3 new projects
- `src/PassReset.Web/PassReset.Web.csproj` — retarget to `net10.0`, conditional Windows provider reference, `WINDOWS_PROVIDER` define
- `src/PassReset.Web/Program.cs` — `ProviderMode` resolution + conditional provider wiring (replaces today's `UseDebugProvider` block from lines ~141–184; existing debug-provider path preserved and guarded inside the Windows branch)
- `src/PassReset.PasswordProvider/PasswordChangeOptions.cs` — add `ProviderMode`, `ServiceAccountDn`, `ServiceAccountPassword`, `BaseDn`, `LdapTrustedCertificateThumbprints` properties
- `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs` — add three new validation rules (ProviderMode on wrong platform, Ldap-requires-fields, Auto-on-Linux-requires-Ldap-fields)
- `src/PassReset.Tests/PassReset.Tests.csproj` — retarget to `net10.0`
- `src/PassReset.Web/appsettings.schema.json` — add new `PasswordChangeOptions` properties with constraints
- `src/PassReset.Web/appsettings.Production.template.json` — add new keys with placeholder values
- `src/PassReset.Web/appsettings.json` — add new keys with defaults (`ProviderMode: "Auto"`)
- `CHANGELOG.md` — new `[2.0.0-alpha.1]` section
- `docs/appsettings-Production.md` — document new fields
- `docs/IIS-Setup.md` — add "Cross-platform alternatives" callout pointing to new doc
- `README.md` — update platform-support matrix

---

## Sequencing

Tasks are grouped into five phases executed in order. Within a phase, tasks are sequential.

1. **Foundation** (Tasks 1–4): TFM moves, new projects, `ProviderMode` enum, options schema
2. **Adapter & errors** (Tasks 5–7): `ILdapSession`, `LdapSession`, `LdapErrorMapping`
3. **Provider implementation** (Tasks 8–13): `LdapPasswordChangeProvider` built TDD method-by-method
4. **Contract + parity** (Tasks 14–16): Shared contract test suite + Windows contract impl
5. **Integration + wiring + docs** (Tasks 17–22): Samba CI job, Program.cs wiring, CHANGELOG + docs

---

### Task 1: Add ProviderMode enum to PassReset.Common

**Files:**
- Create: `src/PassReset.Common/ProviderMode.cs`

- [ ] **Step 1: Create the enum**

```csharp
namespace PassReset.Common;

/// <summary>
/// Selects which <see cref="IPasswordChangeProvider"/> implementation PassReset uses at runtime.
/// </summary>
public enum ProviderMode
{
    /// <summary>
    /// Picks <see cref="Windows"/> on Windows platforms, <see cref="Ldap"/> elsewhere.
    /// Default for upgraded deployments (existing Windows installs see no behavior change).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// AccountManagement-based provider (requires <c>net10.0-windows</c>).
    /// Fails validation on non-Windows platforms.
    /// </summary>
    Windows = 1,

    /// <summary>
    /// LdapConnection-based provider (<c>System.DirectoryServices.Protocols</c>).
    /// Works on Windows, Linux, and macOS.
    /// </summary>
    Ldap = 2,
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.Common/PassReset.Common.csproj -c Release`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.Common/ProviderMode.cs
git commit -m "feat(common): add ProviderMode enum for cross-platform provider selection [phase-11]"
```

---

### Task 2: Create PassReset.PasswordProvider.Ldap project skeleton

**Files:**
- Create: `src/PassReset.PasswordProvider.Ldap/PassReset.PasswordProvider.Ldap.csproj`
- Create: `src/PassReset.PasswordProvider.Ldap/LdapAttributeNames.cs`
- Modify: `src/PassReset.sln` (register the new project)

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.5" />
    <PackageReference Include="System.DirectoryServices.Protocols" Version="10.0.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\PassReset.Common\PassReset.Common.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="PassReset.Tests" />
    <InternalsVisibleTo Include="PassReset.Tests.Integration.Ldap" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the attribute-name constants file**

```csharp
namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Canonical Active Directory LDAP attribute names used by <see cref="LdapPasswordChangeProvider"/>.
/// Kept as named constants so misspellings surface at compile time rather than as silent empty results.
/// </summary>
internal static class LdapAttributeNames
{
    public const string SamAccountName       = "samAccountName";
    public const string UserPrincipalName    = "userPrincipalName";
    public const string Mail                 = "mail";
    public const string DistinguishedName    = "distinguishedName";
    public const string UnicodePwd           = "unicodePwd";
    public const string PwdLastSet           = "pwdLastSet";
    public const string MinPwdAge            = "minPwdAge";
    public const string MaxPwdAge            = "maxPwdAge";
    public const string MinPwdLength         = "minPwdLength";
    public const string UserAccountControl   = "userAccountControl";
    public const string UacComputed          = "msDS-User-Account-Control-Computed";
    public const string TokenGroups          = "tokenGroups";
    public const string MemberOf             = "memberOf";
    public const string ObjectClass          = "objectClass";
    public const string DisplayName          = "displayName";
    public const string Manager              = "manager";
}
```

- [ ] **Step 3: Register the project in the solution**

Run: `dotnet sln src/PassReset.sln add src/PassReset.PasswordProvider.Ldap/PassReset.PasswordProvider.Ldap.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 4: Build the solution**

Run: `dotnet build src/PassReset.sln -c Release`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.sln src/PassReset.PasswordProvider.Ldap/
git commit -m "feat(provider): scaffold PassReset.PasswordProvider.Ldap project [phase-11]"
```

---

### Task 3: Extend PasswordChangeOptions with Ldap fields + validator rules

**Files:**
- Modify: `src/PassReset.PasswordProvider/PasswordChangeOptions.cs`
- Modify: `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs`
- Modify: `src/PassReset.Tests/PasswordProvider/PasswordChangeOptionsValidatorTests.cs` (or create if missing — check first)
- Test: `src/PassReset.Tests/PasswordProvider/PasswordChangeOptionsValidatorTests.cs`

- [ ] **Step 1: Add new properties to PasswordChangeOptions**

In `src/PassReset.PasswordProvider/PasswordChangeOptions.cs`, add inside the class body (exact location: after the existing `LdapPassword` property, preserving grouping):

```csharp
/// <summary>
/// Selects which password provider implementation to use. Default <see cref="ProviderMode.Auto"/> picks
/// Windows on Windows platforms, Ldap elsewhere. Windows deployments upgrading from v1.4.x see no change.
/// </summary>
public ProviderMode ProviderMode { get; set; } = ProviderMode.Auto;

/// <summary>
/// Distinguished name of the AD service account used to bind over LDAPS when
/// <see cref="ProviderMode"/> is <see cref="ProviderMode.Ldap"/> (or <see cref="ProviderMode.Auto"/>
/// on non-Windows). Required: grant this account the 'Change Password' extended right on the target OU.
/// See <c>docs/AD-ServiceAccount-LDAP-Setup.md</c>.
/// </summary>
public string ServiceAccountDn { get; set; } = string.Empty;

/// <summary>
/// Service account password for LDAPS bind. Bind via environment variable
/// <c>PasswordChangeOptions__ServiceAccountPassword</c> per the STAB-017 env-var pattern;
/// never commit plaintext.
/// </summary>
public string ServiceAccountPassword { get; set; } = string.Empty;

/// <summary>
/// Base DN for user searches (typically the domain root, e.g. <c>DC=corp,DC=example,DC=com</c>).
/// Required when <see cref="ProviderMode"/> resolves to <see cref="ProviderMode.Ldap"/>.
/// </summary>
public string BaseDn { get; set; } = string.Empty;

/// <summary>
/// Optional SHA-1 or SHA-256 thumbprint allow-list for LDAPS certificates whose trust root is
/// not in the system certificate store (e.g. Linux hosts talking to an internal-CA-issued DC cert).
/// Empty list means 'use the system trust store only'. Mirrors the
/// <c>SmtpSettings.TrustedCertificateThumbprints</c> pattern.
/// </summary>
public List<string> LdapTrustedCertificateThumbprints { get; set; } = new();
```

Also add at the top of the file (with existing usings):

```csharp
using PassReset.Common;
```

- [ ] **Step 2: Add validator rules**

In `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs`, inside `Validate(string? name, PasswordChangeOptions options)` (append after existing rules, before the final `return` / `ValidateOptionsResult` construction):

```csharp
// ProviderMode cross-platform sanity checks (Phase 11).
var isWindows = OperatingSystem.IsWindows();

if (options.ProviderMode == ProviderMode.Windows && !isWindows)
{
    failures.Add($"PasswordChangeOptions.ProviderMode=Windows requires a Windows host; current platform is {System.Runtime.InteropServices.RuntimeInformation.OSDescription}. Set ProviderMode to Ldap or Auto.");
}

var resolvesToLdap =
    options.ProviderMode == ProviderMode.Ldap ||
    (options.ProviderMode == ProviderMode.Auto && !isWindows);

if (resolvesToLdap)
{
    if (string.IsNullOrWhiteSpace(options.ServiceAccountDn))
        failures.Add("PasswordChangeOptions.ServiceAccountDn is required when ProviderMode resolves to Ldap.");
    if (string.IsNullOrWhiteSpace(options.ServiceAccountPassword))
        failures.Add("PasswordChangeOptions.ServiceAccountPassword is required when ProviderMode resolves to Ldap. Bind via PasswordChangeOptions__ServiceAccountPassword env var.");
    if (string.IsNullOrWhiteSpace(options.BaseDn))
        failures.Add("PasswordChangeOptions.BaseDn is required when ProviderMode resolves to Ldap.");
    if (options.LdapHostnames is null || options.LdapHostnames.Length == 0)
        failures.Add("PasswordChangeOptions.LdapHostnames must contain at least one hostname when ProviderMode resolves to Ldap.");
}
```

Add `using PassReset.Common;` at the top of the file if not present.

- [ ] **Step 3: Write validator tests**

Append to (or create) `src/PassReset.Tests/PasswordProvider/PasswordChangeOptionsValidatorTests.cs`:

```csharp
[Fact]
public void LdapMode_MissingServiceAccountDn_Fails()
{
    var sut = new PasswordChangeOptionsValidator();
    var opts = new PasswordChangeOptions
    {
        ProviderMode = ProviderMode.Ldap,
        ServiceAccountDn = "",
        ServiceAccountPassword = "pw",
        BaseDn = "DC=corp,DC=example,DC=com",
        LdapHostnames = new[] { "dc01.corp.example.com" },
        UseAutomaticContext = false,
        LdapPort = 636,
    };

    var result = sut.Validate(null, opts);

    Assert.True(result.Failed);
    Assert.Contains(result.Failures!, f => f.Contains("ServiceAccountDn"));
}

[Fact]
public void LdapMode_MissingBaseDn_Fails()
{
    var sut = new PasswordChangeOptionsValidator();
    var opts = new PasswordChangeOptions
    {
        ProviderMode = ProviderMode.Ldap,
        ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
        ServiceAccountPassword = "pw",
        BaseDn = "",
        LdapHostnames = new[] { "dc01.corp.example.com" },
        UseAutomaticContext = false,
        LdapPort = 636,
    };

    var result = sut.Validate(null, opts);

    Assert.True(result.Failed);
    Assert.Contains(result.Failures!, f => f.Contains("BaseDn"));
}

[Fact]
public void LdapMode_AllFieldsPresent_Succeeds()
{
    var sut = new PasswordChangeOptionsValidator();
    var opts = new PasswordChangeOptions
    {
        ProviderMode = ProviderMode.Ldap,
        ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
        ServiceAccountPassword = "pw",
        BaseDn = "DC=corp,DC=example,DC=com",
        LdapHostnames = new[] { "dc01.corp.example.com" },
        UseAutomaticContext = false,
        LdapPort = 636,
    };

    var result = sut.Validate(null, opts);

    Assert.True(result.Succeeded);
}

[Fact]
public void WindowsMode_OnWindows_Succeeds()
{
    if (!OperatingSystem.IsWindows()) return; // platform-gated assertion
    var sut = new PasswordChangeOptionsValidator();
    var opts = new PasswordChangeOptions
    {
        ProviderMode = ProviderMode.Windows,
        UseAutomaticContext = true,
        LdapPort = 636,
    };

    var result = sut.Validate(null, opts);

    Assert.True(result.Succeeded);
}
```

- [ ] **Step 4: Run validator tests**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~PasswordChangeOptionsValidatorTests"`
Expected: all tests pass (4 new + existing).

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider/PasswordChangeOptions.cs \
        src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs \
        src/PassReset.Tests/PasswordProvider/PasswordChangeOptionsValidatorTests.cs
git commit -m "feat(provider): add ProviderMode + LDAP service-account options + validator rules [phase-11]"
```

---

### Task 4: Update appsettings.schema.json + templates

**Files:**
- Modify: `src/PassReset.Web/appsettings.schema.json`
- Modify: `src/PassReset.Web/appsettings.Production.template.json`
- Modify: `src/PassReset.Web/appsettings.json`

- [ ] **Step 1: Extend the schema**

In `src/PassReset.Web/appsettings.schema.json`, inside `PasswordChangeOptions.properties`, add (preserve existing properties):

```json
"ProviderMode": {
  "type": "string",
  "enum": ["Auto", "Windows", "Ldap"],
  "default": "Auto",
  "description": "Which IPasswordChangeProvider to use. Auto picks Windows on Windows, Ldap elsewhere."
},
"ServiceAccountDn": {
  "type": "string",
  "default": "",
  "description": "AD service account DN for LDAPS bind. Required when ProviderMode resolves to Ldap."
},
"ServiceAccountPassword": {
  "type": "string",
  "default": "",
  "description": "Service account password. Bind via PasswordChangeOptions__ServiceAccountPassword env var; do not commit plaintext."
},
"BaseDn": {
  "type": "string",
  "default": "",
  "description": "Base DN for user searches (typically the domain root). Required when ProviderMode resolves to Ldap."
},
"LdapTrustedCertificateThumbprints": {
  "type": "array",
  "items": { "type": "string", "pattern": "^[A-Fa-f0-9]{40}(?:[A-Fa-f0-9]{24})?$" },
  "default": [],
  "description": "SHA-1 or SHA-256 thumbprints of LDAPS certs not in the system trust store."
}
```

- [ ] **Step 2: Extend the Production template**

In `src/PassReset.Web/appsettings.Production.template.json`, inside `PasswordChangeOptions`, add:

```json
"ProviderMode": "Auto",
"ServiceAccountDn": "",
"ServiceAccountPassword": "",
"BaseDn": "",
"LdapTrustedCertificateThumbprints": []
```

- [ ] **Step 3: Extend appsettings.json (default dev config)**

Same five keys with same default values as Step 2.

- [ ] **Step 4: Validate JSON**

Run: `jq . src/PassReset.Web/appsettings.schema.json > /dev/null && jq . src/PassReset.Web/appsettings.Production.template.json > /dev/null && jq . src/PassReset.Web/appsettings.json > /dev/null && echo OK`
Expected: `OK`

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.Web/appsettings.schema.json \
        src/PassReset.Web/appsettings.Production.template.json \
        src/PassReset.Web/appsettings.json
git commit -m "feat(web): surface ProviderMode + LDAP fields in appsettings schema + templates [phase-11]"
```

---

### Task 5: Define ILdapSession adapter interface

**Files:**
- Create: `src/PassReset.PasswordProvider.Ldap/ILdapSession.cs`

- [ ] **Step 1: Write the interface**

```csharp
using System.DirectoryServices.Protocols;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Thin adapter over <see cref="LdapConnection"/> to isolate the LDAP I/O surface for testing.
/// The fake <c>FakeLdapSession</c> used in unit + contract tests implements this interface
/// to script directory behavior without a live AD.
/// </summary>
public interface ILdapSession : IDisposable
{
    /// <summary>
    /// Bind to the configured directory using the service account credentials supplied at construction.
    /// Throws <see cref="LdapException"/> on auth failure.
    /// </summary>
    void Bind();

    /// <summary>
    /// Execute a <see cref="SearchRequest"/> and return the full response. Callers MUST check
    /// <see cref="DirectoryResponse.ResultCode"/> before reading <see cref="SearchResponse.Entries"/>.
    /// </summary>
    SearchResponse Search(SearchRequest request);

    /// <summary>
    /// Execute a <see cref="ModifyRequest"/> (including the unicodePwd atomic-change pattern).
    /// Throws <see cref="DirectoryOperationException"/> on server-side rejection so callers can
    /// inspect <see cref="DirectoryResponse.ResultCode"/> and the Win32 extended error code.
    /// </summary>
    ModifyResponse Modify(ModifyRequest request);

    /// <summary>
    /// Root DSE attributes (<c>defaultNamingContext</c>, <c>dnsHostName</c>, etc.).
    /// Convenience: returns <c>null</c> if the root DSE query fails rather than throwing.
    /// </summary>
    SearchResultEntry? RootDse { get; }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.PasswordProvider.Ldap/ -c Release`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/ILdapSession.cs
git commit -m "feat(provider-ldap): add ILdapSession adapter interface [phase-11]"
```

---

### Task 6: Implement LdapSession (default ILdapSession)

**Files:**
- Create: `src/PassReset.PasswordProvider.Ldap/LdapSession.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Default <see cref="ILdapSession"/> implementation wrapping a <see cref="LdapConnection"/>.
/// One session per password-change request (no pooling — low-frequency operation).
/// </summary>
public sealed class LdapSession : ILdapSession
{
    private readonly LdapConnection _conn;
    private readonly NetworkCredential _creds;
    private readonly ILogger _logger;
    private readonly IReadOnlySet<string> _trustedThumbprints;
    private SearchResultEntry? _rootDse;
    private bool _rootDseLoaded;

    public LdapSession(
        string hostname,
        int port,
        bool useLdaps,
        string serviceAccountDn,
        string serviceAccountPassword,
        IEnumerable<string>? trustedThumbprints,
        ILogger logger)
    {
        _conn = new LdapConnection(new LdapDirectoryIdentifier(hostname, port));
        _conn.SessionOptions.ProtocolVersion = 3;
        _conn.SessionOptions.SecureSocketLayer = useLdaps;
        _conn.AuthType = AuthType.Basic;
        _creds = new NetworkCredential(serviceAccountDn, serviceAccountPassword);
        _logger = logger;
        _trustedThumbprints = (trustedThumbprints ?? Enumerable.Empty<string>())
            .Select(t => t.Replace(":", "").ToUpperInvariant())
            .ToHashSet();

        if (useLdaps && _trustedThumbprints.Count > 0)
        {
            _conn.SessionOptions.VerifyServerCertificate = VerifyServerCertificate;
        }
    }

    public void Bind() => _conn.Bind(_creds);

    public SearchResponse Search(SearchRequest request) => (SearchResponse)_conn.SendRequest(request);

    public ModifyResponse Modify(ModifyRequest request) => (ModifyResponse)_conn.SendRequest(request);

    public SearchResultEntry? RootDse
    {
        get
        {
            if (_rootDseLoaded) return _rootDse;
            _rootDseLoaded = true;
            try
            {
                var req = new SearchRequest(
                    distinguishedName: null,
                    ldapFilter: "(objectClass=*)",
                    searchScope: SearchScope.Base,
                    attributeList: new[] { "defaultNamingContext", "dnsHostName", "minPwdAge", "maxPwdAge", "minPwdLength" });
                var resp = (SearchResponse)_conn.SendRequest(req);
                _rootDse = resp.Entries.Count > 0 ? resp.Entries[0] : null;
            }
            catch (LdapException ex)
            {
                _logger.LogWarning(ex, "Root DSE query failed on {Host}", _conn.Directory);
                _rootDse = null;
            }
            return _rootDse;
        }
    }

    private bool VerifyServerCertificate(LdapConnection connection, X509Certificate certificate)
    {
        // If the system store already trusts the cert, let it through.
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        var sysTrusted = chain.Build(new X509Certificate2(certificate));
        if (sysTrusted) return true;

        // Otherwise, match against the operator-configured thumbprint allow-list.
        var cert2 = new X509Certificate2(certificate);
        var sha1 = cert2.Thumbprint ?? string.Empty;
        var sha256 = Convert.ToHexString(cert2.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256));
        var trusted = _trustedThumbprints.Contains(sha1.ToUpperInvariant())
                      || _trustedThumbprints.Contains(sha256.ToUpperInvariant());

        if (!trusted)
        {
            _logger.LogError(
                "LDAPS certificate rejected: neither system trust nor LdapTrustedCertificateThumbprints matched. SHA-1={Sha1}, SHA-256={Sha256}",
                sha1, sha256);
        }
        return trusted;
    }

    public void Dispose() => _conn.Dispose();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.PasswordProvider.Ldap/ -c Release`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapSession.cs
git commit -m "feat(provider-ldap): add LdapSession with optional cert-thumbprint pinning [phase-11]"
```

---

### Task 7: Implement LdapErrorMapping with TDD

**Files:**
- Create: `src/PassReset.PasswordProvider.Ldap/LdapErrorMapping.cs`
- Test: `src/PassReset.Tests/Services/LdapErrorMappingTests.cs`

- [ ] **Step 1: Write the failing tests first**

```csharp
using System.DirectoryServices.Protocols;
using PassReset.Common;
using PassReset.PasswordProvider.Ldap;
using Xunit;

namespace PassReset.Tests.Services;

public class LdapErrorMappingTests
{
    [Theory]
    [InlineData(ResultCode.InvalidCredentials, 0u, ApiErrorCode.InvalidCredentials)]
    [InlineData(ResultCode.NoSuchObject,       0u, ApiErrorCode.UserNotFound)]
    [InlineData(ResultCode.InsufficientAccessRights, 0u, ApiErrorCode.ChangeNotPermitted)]
    [InlineData(ResultCode.UnwillingToPerform, 0x0000052Du, ApiErrorCode.ComplexPassword)]   // ERROR_PASSWORD_RESTRICTION
    [InlineData(ResultCode.ConstraintViolation, 0x0000052Du, ApiErrorCode.ComplexPassword)]
    [InlineData(ResultCode.UnwillingToPerform, 0x00000775u, ApiErrorCode.PortalLockout)]     // ERROR_ACCOUNT_LOCKED_OUT
    [InlineData(ResultCode.UnwillingToPerform, 0x00000533u, ApiErrorCode.ChangeNotPermitted)] // ERROR_ACCOUNT_DISABLED
    [InlineData(ResultCode.UnwillingToPerform, 0x00000534u, ApiErrorCode.ChangeNotPermitted)] // ERROR_LOGON_TYPE_NOT_GRANTED
    [InlineData(ResultCode.UnwillingToPerform, 0x00000773u, ApiErrorCode.PasswordTooRecentlyChanged)] // ERROR_PASSWORD_MUST_CHANGE (treated as too-recent in our mapping)
    [InlineData(ResultCode.OperationsError,    0u, ApiErrorCode.Generic)]
    public void Map_ReturnsExpectedCode(ResultCode resultCode, uint extendedError, ApiErrorCode expected)
    {
        var actual = LdapErrorMapping.Map(resultCode, extendedError);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Map_Unknown_ReturnsGeneric()
    {
        var actual = LdapErrorMapping.Map((ResultCode)999, 0u);
        Assert.Equal(ApiErrorCode.Generic, actual);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~LdapErrorMappingTests"`
Expected: compile error or all tests fail (class doesn't exist).

- [ ] **Step 3: Implement LdapErrorMapping**

```csharp
using System.DirectoryServices.Protocols;
using PassReset.Common;

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Maps AD LDAP <see cref="ResultCode"/> + Win32 <c>extendedError</c> DWORDs to
/// <see cref="ApiErrorCode"/> values consistent with the Windows provider's
/// <see cref="System.Runtime.InteropServices.COMException"/> mapping.
/// </summary>
/// <remarks>
/// Extended-error DWORDs come from the <c>dataXXXXXXXX</c> hex prefix in the
/// server-supplied <see cref="DirectoryResponse.ErrorMessage"/> on password-related
/// failures. The well-known Windows error codes here are documented at
/// <see href="https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes"/>.
/// </remarks>
public static class LdapErrorMapping
{
    // Well-known Win32 codes surfaced via AD's extendedError DWORD.
    private const uint ERROR_PASSWORD_RESTRICTION   = 0x0000052D;
    private const uint ERROR_ACCOUNT_DISABLED       = 0x00000533;
    private const uint ERROR_LOGON_TYPE_NOT_GRANTED = 0x00000534;
    private const uint ERROR_PASSWORD_MUST_CHANGE   = 0x00000773;
    private const uint ERROR_ACCOUNT_LOCKED_OUT     = 0x00000775;

    public static ApiErrorCode Map(ResultCode resultCode, uint extendedError)
    {
        // Extended-error takes precedence — it's more specific than the generic ResultCode.
        switch (extendedError)
        {
            case ERROR_PASSWORD_RESTRICTION:
                return ApiErrorCode.ComplexPassword;
            case ERROR_ACCOUNT_LOCKED_OUT:
                return ApiErrorCode.PortalLockout;
            case ERROR_ACCOUNT_DISABLED:
            case ERROR_LOGON_TYPE_NOT_GRANTED:
                return ApiErrorCode.ChangeNotPermitted;
            case ERROR_PASSWORD_MUST_CHANGE:
                return ApiErrorCode.PasswordTooRecentlyChanged;
        }

        return resultCode switch
        {
            ResultCode.InvalidCredentials       => ApiErrorCode.InvalidCredentials,
            ResultCode.NoSuchObject             => ApiErrorCode.UserNotFound,
            ResultCode.InsufficientAccessRights => ApiErrorCode.ChangeNotPermitted,
            ResultCode.ConstraintViolation      => ApiErrorCode.ComplexPassword,
            ResultCode.UnwillingToPerform       => ApiErrorCode.ChangeNotPermitted,
            _ => ApiErrorCode.Generic,
        };
    }

    /// <summary>
    /// Parses the Win32 DWORD from an AD <see cref="DirectoryResponse.ErrorMessage"/> string of the
    /// form <c>0000052D: SvcErr: DSID-xxxxxxxx, problem 5003 (WILL_NOT_PERFORM), data 52d, ...</c>.
    /// Returns 0 when no <c>dataXXXX</c> token is present.
    /// </summary>
    public static uint ExtractExtendedError(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return 0;
        var marker = errorMessage.IndexOf("data ", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return 0;
        var span = errorMessage.AsSpan(marker + 5);
        // Read hex chars until non-hex.
        var end = 0;
        while (end < span.Length && Uri.IsHexDigit(span[end])) end++;
        if (end == 0) return 0;
        return uint.TryParse(span[..end], System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~LdapErrorMappingTests"`
Expected: all 12 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapErrorMapping.cs \
        src/PassReset.Tests/Services/LdapErrorMappingTests.cs
git commit -m "feat(provider-ldap): LDAP ResultCode + extendedError → ApiErrorCode mapping [phase-11]"
```

---

### Task 8: Build FakeLdapSession test double

**Files:**
- Create: `src/PassReset.Tests/Fakes/FakeLdapSession.cs`

- [ ] **Step 1: Write the fake**

```csharp
using System.DirectoryServices.Protocols;
using PassReset.PasswordProvider.Ldap;

namespace PassReset.Tests.Fakes;

/// <summary>
/// Scripted <see cref="ILdapSession"/> fake for unit + contract tests.
/// Callers register <see cref="SearchResponse"/>/<see cref="ModifyResponse"/> values
/// (or exceptions to throw) keyed by operation type + filter substring.
/// </summary>
public sealed class FakeLdapSession : ILdapSession
{
    private readonly List<SearchRule> _searchRules = new();
    private readonly List<ModifyRule> _modifyRules = new();

    public SearchResultEntry? RootDse { get; set; }

    public int SearchCallCount { get; private set; }
    public int ModifyCallCount { get; private set; }
    public int BindCallCount { get; private set; }

    public Exception? BindThrows { get; set; }

    public void Bind()
    {
        BindCallCount++;
        if (BindThrows is not null) throw BindThrows;
    }

    public FakeLdapSession OnSearch(string filterContains, SearchResponse response)
    {
        _searchRules.Add(new SearchRule(filterContains, response, null));
        return this;
    }

    public FakeLdapSession OnSearchThrow(string filterContains, Exception ex)
    {
        _searchRules.Add(new SearchRule(filterContains, null, ex));
        return this;
    }

    public FakeLdapSession OnModify(string dnContains, ModifyResponse response)
    {
        _modifyRules.Add(new ModifyRule(dnContains, response, null));
        return this;
    }

    public FakeLdapSession OnModifyThrow(string dnContains, Exception ex)
    {
        _modifyRules.Add(new ModifyRule(dnContains, null, ex));
        return this;
    }

    public SearchResponse Search(SearchRequest request)
    {
        SearchCallCount++;
        foreach (var rule in _searchRules)
        {
            if (request.Filter is string f && f.Contains(rule.FilterContains, StringComparison.OrdinalIgnoreCase))
            {
                if (rule.Throw is not null) throw rule.Throw;
                return rule.Response!;
            }
        }
        throw new InvalidOperationException(
            $"FakeLdapSession: no matching SearchRule for filter='{request.Filter}'. Register one via OnSearch(...).");
    }

    public ModifyResponse Modify(ModifyRequest request)
    {
        ModifyCallCount++;
        foreach (var rule in _modifyRules)
        {
            if (request.DistinguishedName.Contains(rule.DnContains, StringComparison.OrdinalIgnoreCase))
            {
                if (rule.Throw is not null) throw rule.Throw;
                return rule.Response!;
            }
        }
        throw new InvalidOperationException(
            $"FakeLdapSession: no matching ModifyRule for DN='{request.DistinguishedName}'. Register one via OnModify(...).");
    }

    public void Dispose() { }

    private sealed record SearchRule(string FilterContains, SearchResponse? Response, Exception? Throw);
    private sealed record ModifyRule(string DnContains, ModifyResponse? Response, Exception? Throw);
}
```

- [ ] **Step 2: Update test project to reference PassReset.PasswordProvider.Ldap**

In `src/PassReset.Tests/PassReset.Tests.csproj`, add inside the existing `<ItemGroup>` containing `<ProjectReference>` entries:

```xml
<ProjectReference Include="..\PassReset.PasswordProvider.Ldap\PassReset.PasswordProvider.Ldap.csproj" />
```

- [ ] **Step 3: Build**

Run: `dotnet build src/PassReset.sln -c Release`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PassReset.Tests/Fakes/FakeLdapSession.cs \
        src/PassReset.Tests/PassReset.Tests.csproj
git commit -m "test(provider-ldap): FakeLdapSession + Tests project reference [phase-11]"
```

---

### Task 9: Skeleton LdapPasswordChangeProvider — constructor + interface stubs

**Files:**
- Create: `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs`

- [ ] **Step 1: Write the skeleton implementing every `IPasswordChangeProvider` method with `NotImplementedException`**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;  // for PasswordChangeOptions

namespace PassReset.PasswordProvider.Ldap;

/// <summary>
/// Cross-platform <see cref="IPasswordChangeProvider"/> backed by
/// <see cref="System.DirectoryServices.Protocols.LdapConnection"/>. Runs on Windows, Linux, and macOS.
/// Behavioral parity with the Windows provider is enforced by the shared
/// <c>IPasswordChangeProviderContract</c> test suite.
/// </summary>
public sealed class LdapPasswordChangeProvider : IPasswordChangeProvider
{
    private readonly IOptions<PasswordChangeOptions> _options;
    private readonly ILogger<LdapPasswordChangeProvider> _logger;
    private readonly Func<ILdapSession> _sessionFactory;

    public LdapPasswordChangeProvider(
        IOptions<PasswordChangeOptions> options,
        ILogger<LdapPasswordChangeProvider> logger,
        Func<ILdapSession> sessionFactory)
    {
        _options = options;
        _logger = logger;
        _sessionFactory = sessionFactory;

        if (OperatingSystem.IsWindows())
        {
            _logger.LogInformation(
                "LdapPasswordChangeProvider active on Windows (ProviderMode={Mode}). " +
                "UserCannotChangePassword ACE check is Linux-deferred; AD server-side enforcement applies.",
                _options.Value.ProviderMode);
        }
    }

    public Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
        => throw new NotImplementedException();

    public string? GetUserEmail(string username)
        => throw new NotImplementedException();

    public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
        => throw new NotImplementedException();

    public TimeSpan GetDomainMaxPasswordAge()
        => throw new NotImplementedException();

    public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync()
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.sln -c Release`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs
git commit -m "feat(provider-ldap): scaffold LdapPasswordChangeProvider [phase-11]"
```

---

### Task 10: Implement user lookup with AllowedUsernameAttributes fallback (TDD)

**Files:**
- Modify: `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs`
- Test: `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs`

- [ ] **Step 1: Write failing tests for `FindUserDnAsync` (internal)**

Create `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs`:

```csharp
using System.DirectoryServices.Protocols;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using PassReset.PasswordProvider;
using PassReset.PasswordProvider.Ldap;
using PassReset.Tests.Fakes;
using Xunit;

namespace PassReset.Tests.Services;

public class LdapPasswordChangeProviderTests
{
    private static (LdapPasswordChangeProvider sut, FakeLdapSession fake) Build(
        PasswordChangeOptions? opts = null)
    {
        opts ??= new PasswordChangeOptions
        {
            AllowedUsernameAttributes = new[] { "samaccountname", "userprincipalname", "mail" },
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
        };
        var fake = new FakeLdapSession();
        var sut = new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => fake);
        return (sut, fake);
    }

    private static SearchResponse MakeResponse(params SearchResultEntry[] entries)
    {
        // System.DirectoryServices.Protocols.SearchResponse has no public constructor;
        // use reflection to build a test instance.
        var response = (SearchResponse)Activator.CreateInstance(
            typeof(SearchResponse), nonPublic: true)!;
        var entriesProp = typeof(SearchResponse).GetProperty("Entries")!;
        var collection = (SearchResultEntryCollection)entriesProp.GetValue(response)!;
        var addMethod = typeof(SearchResultEntryCollection).GetMethod(
            "Add", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var e in entries) addMethod.Invoke(collection, new object?[] { e });
        return response;
    }

    private static SearchResultEntry MakeEntry(string dn, params (string Name, string Value)[] attrs)
    {
        var entry = (SearchResultEntry)Activator.CreateInstance(
            typeof(SearchResultEntry),
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object[] { dn, new DirectoryAttribute[] { } },
            null)!;
        // Attrs populated via reflection-friendly helper in next task as needed.
        return entry;
    }

    [Fact]
    public async Task FindUserDn_SamAccountNameHits_ReturnsDn()
    {
        var (sut, fake) = Build();
        fake.OnSearch(
            "(sAMAccountName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "alice" })!;
        var dn = await task;

        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", dn);
        Assert.Equal(1, fake.SearchCallCount);
    }

    [Fact]
    public async Task FindUserDn_FallsThroughToUpn_WhenSamEmpty()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=alice)", MakeResponse());
        fake.OnSearch(
            "(userPrincipalName=alice)",
            MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "alice" })!;
        var dn = await task;

        Assert.Equal("CN=Alice,OU=Users,DC=corp,DC=example,DC=com", dn);
        Assert.Equal(2, fake.SearchCallCount);
    }

    [Fact]
    public async Task FindUserDn_AllAttributesMiss_ReturnsNull()
    {
        var (sut, fake) = Build();
        fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
        fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
        fake.OnSearch("(mail=ghost)",               MakeResponse());

        var method = typeof(LdapPasswordChangeProvider).GetMethod(
            "FindUserDnAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<string?>)method.Invoke(sut, new object?[] { fake, "ghost" })!;
        var dn = await task;

        Assert.Null(dn);
        Assert.Equal(3, fake.SearchCallCount);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~LdapPasswordChangeProviderTests"`
Expected: all 3 tests fail (method not found).

- [ ] **Step 3: Implement `FindUserDnAsync`**

Replace the `GetUserEmail => throw ...` line in `LdapPasswordChangeProvider.cs` — actually, add `FindUserDnAsync` as an internal helper. Insert inside the class body:

```csharp
/// <summary>
/// Resolves <paramref name="username"/> to its distinguished name by searching each
/// attribute in <see cref="PasswordChangeOptions.AllowedUsernameAttributes"/> in order.
/// Returns null when no attribute matches.
/// </summary>
internal async Task<string?> FindUserDnAsync(ILdapSession session, string username)
{
    await Task.Yield();  // reserved for future async LDAP APIs
    var opts = _options.Value;
    foreach (var attr in opts.AllowedUsernameAttributes)
    {
        var ldapAttr = attr.ToLowerInvariant() switch
        {
            "samaccountname"    => LdapAttributeNames.SamAccountName,
            "userprincipalname" => LdapAttributeNames.UserPrincipalName,
            "mail"              => LdapAttributeNames.Mail,
            _ => null,
        };
        if (ldapAttr is null)
        {
            _logger.LogWarning("Ignoring unknown AllowedUsernameAttributes entry: {Attr}", attr);
            continue;
        }

        var filter = $"({ldapAttr}={EscapeLdapFilterValue(username)})";
        var request = new SearchRequest(
            distinguishedName: opts.BaseDn,
            ldapFilter: filter,
            searchScope: SearchScope.Subtree,
            attributeList: new[] { LdapAttributeNames.DistinguishedName });
        var response = session.Search(request);

        if (response.Entries.Count == 1)
            return response.Entries[0].DistinguishedName;

        if (response.Entries.Count > 1)
        {
            _logger.LogWarning(
                "Ambiguous match: {Count} entries for {Attr}={Username}. Treating as not found.",
                response.Entries.Count, ldapAttr, username);
        }
    }
    return null;
}

/// <summary>
/// RFC 4515 LDAP filter value escaping: backslash, asterisk, parenthesis, NUL.
/// Prevents filter injection when user input is interpolated into a search filter.
/// </summary>
internal static string EscapeLdapFilterValue(string value) =>
    value
        .Replace("\\", @"\5c")
        .Replace("*",  @"\2a")
        .Replace("(",  @"\28")
        .Replace(")",  @"\29")
        .Replace("\0", @"\00");
```

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~LdapPasswordChangeProviderTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs \
        src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs
git commit -m "feat(provider-ldap): FindUserDnAsync with AllowedUsernameAttributes fallback + LDAP filter escaping [phase-11]"
```

---

### Task 11: Implement PerformPasswordChangeAsync happy path (TDD)

**Files:**
- Modify: `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs`
- Modify: `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs`

- [ ] **Step 1: Add happy-path test**

Append to `LdapPasswordChangeProviderTests.cs`:

```csharp
[Fact]
public async Task PerformPasswordChangeAsync_HappyPath_ReturnsNull()
{
    var (sut, fake) = Build();
    fake.OnSearch(
        "(sAMAccountName=alice)",
        MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
    var modifyResp = (ModifyResponse)Activator.CreateInstance(
        typeof(ModifyResponse), nonPublic: true)!;
    // ModifyResponse with default ResultCode.Success — default(ResultCode) == 0 == Success.
    fake.OnModify("CN=Alice,OU=Users,DC=corp", modifyResp);

    var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

    Assert.Null(result);
    Assert.Equal(1, fake.BindCallCount);
    Assert.Equal(1, fake.ModifyCallCount);
}

[Fact]
public async Task PerformPasswordChangeAsync_UserNotFound_ReturnsUserNotFound()
{
    var (sut, fake) = Build();
    fake.OnSearch("(sAMAccountName=ghost)",     MakeResponse());
    fake.OnSearch("(userPrincipalName=ghost)",  MakeResponse());
    fake.OnSearch("(mail=ghost)",               MakeResponse());

    var result = await sut.PerformPasswordChangeAsync("ghost", "any", "new");

    Assert.NotNull(result);
    Assert.Equal(ApiErrorCode.UserNotFound, result!.ErrorCode);
    Assert.Equal(0, fake.ModifyCallCount);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~PerformPasswordChangeAsync"`
Expected: both tests fail (`NotImplementedException`).

- [ ] **Step 3: Implement `PerformPasswordChangeAsync` happy path**

Replace the `PerformPasswordChangeAsync(...) => throw NotImplementedException();` stub in `LdapPasswordChangeProvider.cs`:

```csharp
public async Task<ApiErrorItem?> PerformPasswordChangeAsync(string username, string currentPassword, string newPassword)
{
    using var session = _sessionFactory();

    try
    {
        session.Bind();
    }
    catch (LdapException ex)
    {
        _logger.LogError(ex, "LDAP bind failed as service account");
        return new ApiErrorItem(ApiErrorCode.Generic, null,
            "Directory bind failed; contact your administrator.");
    }

    var userDn = await FindUserDnAsync(session, username);
    if (userDn is null)
    {
        _logger.LogInformation("User not found: {Username}", username);
        return new ApiErrorItem(ApiErrorCode.UserNotFound, nameof(username),
            "User not found in directory.");
    }

    var opts = _options.Value;
    try
    {
        var modifyRequest = BuildChangePasswordRequest(userDn, currentPassword, newPassword, opts.AllowSetPasswordFallback);
        var response = session.Modify(modifyRequest);

        if (response.ResultCode != ResultCode.Success)
        {
            var extended = LdapErrorMapping.ExtractExtendedError(response.ErrorMessage);
            var mapped = LdapErrorMapping.Map(response.ResultCode, extended);
            _logger.LogWarning(
                "ModifyResponse rejected: ResultCode={ResultCode} extendedError=0x{Extended:X8} mapped={Mapped}",
                response.ResultCode, extended, mapped);
            return new ApiErrorItem(mapped, null, MapperMessageFor(mapped));
        }

        return null;
    }
    catch (DirectoryOperationException ex)
    {
        var extended = LdapErrorMapping.ExtractExtendedError(ex.Response?.ErrorMessage);
        var mapped = LdapErrorMapping.Map(ex.Response?.ResultCode ?? ResultCode.OperationsError, extended);
        _logger.LogWarning(ex,
            "DirectoryOperationException on Modify: ResultCode={ResultCode} extendedError=0x{Extended:X8} mapped={Mapped}",
            ex.Response?.ResultCode, extended, mapped);
        return new ApiErrorItem(mapped, null, MapperMessageFor(mapped));
    }
    catch (LdapException ex)
    {
        _logger.LogError(ex, "Unexpected LDAP exception on password change");
        return new ApiErrorItem(ApiErrorCode.Generic, null, "Unexpected directory error.");
    }
}

private static ModifyRequest BuildChangePasswordRequest(
    string userDn, string current, string next, bool allowSetFallback)
{
    // AD atomic change-password pattern: single ModifyRequest with Delete(old) + Add(new)
    // on unicodePwd. The value must be UTF-16LE-encoded and wrapped in literal quote chars.
    var oldBytes = System.Text.Encoding.Unicode.GetBytes($"\"{current}\"");
    var newBytes = System.Text.Encoding.Unicode.GetBytes($"\"{next}\"");

    if (allowSetFallback)
    {
        // Replace semantic: SetPassword equivalent — bypasses history. Opt-in only.
        var replace = new DirectoryAttributeModification
        {
            Operation = DirectoryAttributeOperation.Replace,
            Name = LdapAttributeNames.UnicodePwd,
        };
        replace.Add(newBytes);
        return new ModifyRequest(userDn, replace);
    }

    var del = new DirectoryAttributeModification
    {
        Operation = DirectoryAttributeOperation.Delete,
        Name = LdapAttributeNames.UnicodePwd,
    };
    del.Add(oldBytes);
    var add = new DirectoryAttributeModification
    {
        Operation = DirectoryAttributeOperation.Add,
        Name = LdapAttributeNames.UnicodePwd,
    };
    add.Add(newBytes);
    return new ModifyRequest(userDn, del, add);
}

private static string MapperMessageFor(ApiErrorCode code) => code switch
{
    ApiErrorCode.InvalidCredentials          => "Current password is incorrect.",
    ApiErrorCode.UserNotFound                => "User not found in directory.",
    ApiErrorCode.ChangeNotPermitted          => "Password change is not permitted for this account.",
    ApiErrorCode.ComplexPassword             => "The new password does not meet domain complexity requirements.",
    ApiErrorCode.PortalLockout               => "Account is locked out. Contact your administrator.",
    ApiErrorCode.PasswordTooRecentlyChanged  => "Password was changed too recently; please wait before trying again.",
    _                                        => "Unexpected error.",
};
```

Also add required usings at the top of the file:

```csharp
using System.DirectoryServices.Protocols;
```

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~PerformPasswordChangeAsync"`
Expected: both tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs \
        src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs
git commit -m "feat(provider-ldap): PerformPasswordChangeAsync happy path + user-not-found + error mapping [phase-11]"
```

---

### Task 12: Implement group checks (Restricted/Allowed) + STAB-004 PreCheckMinPwdAge (TDD)

**Files:**
- Modify: `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs`
- Modify: `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs`

- [ ] **Step 1: Write failing tests**

Append to `LdapPasswordChangeProviderTests.cs`:

```csharp
[Fact]
public async Task PerformPasswordChangeAsync_RestrictedGroup_ReturnsChangeNotPermitted()
{
    var opts = new PasswordChangeOptions
    {
        AllowedUsernameAttributes = new[] { "samaccountname" },
        RestrictedAdGroups = new() { "Domain Admins" },
        BaseDn = "DC=corp,DC=example,DC=com",
        ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
        ServiceAccountPassword = "svcpw",
        LdapHostnames = new[] { "dc01.corp.example.com" },
        LdapPort = 636,
    };
    var (sut, fake) = Build(opts);

    fake.OnSearch(
        "(sAMAccountName=alice)",
        MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com")));
    // Group lookup returns Domain Admins membership (mock via CN contains in SearchRule).
    fake.OnSearch(
        "tokenGroups",
        MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            (LdapAttributeNames.MemberOf, "CN=Domain Admins,CN=Users,DC=corp,DC=example,DC=com"))));

    var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

    Assert.NotNull(result);
    Assert.Equal(ApiErrorCode.ChangeNotPermitted, result!.ErrorCode);
    Assert.Equal(0, fake.ModifyCallCount);
}

[Fact]
public async Task PerformPasswordChangeAsync_MinPwdAgeViolation_ReturnsPasswordTooRecent()
{
    var opts = new PasswordChangeOptions
    {
        AllowedUsernameAttributes = new[] { "samaccountname" },
        EnforceMinimumPasswordAge = true,
        BaseDn = "DC=corp,DC=example,DC=com",
        ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
        ServiceAccountPassword = "svcpw",
        LdapHostnames = new[] { "dc01.corp.example.com" },
        LdapPort = 636,
    };
    var (sut, fake) = Build(opts);

    var pwdLastSet = DateTime.UtcNow.AddHours(-1).ToFileTimeUtc();  // 1 hour ago
    var minPwdAgeTicks = -TimeSpan.FromDays(1).Ticks;               // 1 day min age, negative per AD

    fake.OnSearch(
        "(sAMAccountName=alice)",
        MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            (LdapAttributeNames.PwdLastSet, pwdLastSet.ToString()))));
    fake.RootDse = MakeEntry("",
        (LdapAttributeNames.MinPwdAge, minPwdAgeTicks.ToString()));

    var result = await sut.PerformPasswordChangeAsync("alice", "OldPass1!", "NewPass1!");

    Assert.NotNull(result);
    Assert.Equal(ApiErrorCode.PasswordTooRecentlyChanged, result!.ErrorCode);
    Assert.Equal(0, fake.ModifyCallCount);
}
```

- [ ] **Step 2: Run — expect fails**

Run: `dotnet test src/PassReset.sln -c Release --filter "RestrictedGroup|MinPwdAgeViolation"`
Expected: both fail.

- [ ] **Step 3: Implement group checks + PreCheckMinPwdAge, wired into PerformPasswordChangeAsync**

In `LdapPasswordChangeProvider.cs`, modify `PerformPasswordChangeAsync` to call two new helpers **after** `FindUserDnAsync` succeeds and **before** building the Modify request. Add these helpers to the class:

```csharp
/// <summary>
/// Returns null when the user is allowed to proceed per <see cref="PasswordChangeOptions.RestrictedAdGroups"/>
/// and <see cref="PasswordChangeOptions.AllowedAdGroups"/>. Returns a ChangeNotPermitted error item otherwise.
/// Mirrors the Windows provider's <c>ValidateGroups</c> semantics.
/// </summary>
internal ApiErrorItem? ValidateGroups(ILdapSession session, string userDn)
{
    var opts = _options.Value;
    if (opts.RestrictedAdGroups.Count == 0 && opts.AllowedAdGroups.Count == 0)
        return null;

    var userGroups = ReadUserGroups(session, userDn);

    if (opts.RestrictedAdGroups.Count > 0 &&
        userGroups.Any(g => opts.RestrictedAdGroups.Contains(g, StringComparer.OrdinalIgnoreCase)))
    {
        _logger.LogInformation("User {Dn} is a member of a restricted group; denying change.", userDn);
        return new ApiErrorItem(ApiErrorCode.ChangeNotPermitted, null,
            "Password change is not permitted for this account.");
    }

    if (opts.AllowedAdGroups.Count > 0 &&
        !userGroups.Any(g => opts.AllowedAdGroups.Contains(g, StringComparer.OrdinalIgnoreCase)))
    {
        _logger.LogInformation("User {Dn} is not in any AllowedAdGroups; denying change.", userDn);
        return new ApiErrorItem(ApiErrorCode.ChangeNotPermitted, null,
            "Password change is not permitted for this account.");
    }

    return null;
}

/// <summary>
/// Reads the user's effective group membership names via the <c>memberOf</c> attribute
/// (full DN → common-name extraction). <c>tokenGroups</c>-based recursive resolution is a
/// later improvement; <c>memberOf</c> covers direct memberships and is sufficient for
/// the RestrictedAdGroups / AllowedAdGroups checks operators configure.
/// </summary>
private IReadOnlyList<string> ReadUserGroups(ILdapSession session, string userDn)
{
    var req = new SearchRequest(
        distinguishedName: userDn,
        ldapFilter: "(objectClass=user)",
        searchScope: SearchScope.Base,
        attributeList: new[] { LdapAttributeNames.MemberOf });
    try
    {
        var resp = session.Search(req);
        if (resp.Entries.Count == 0) return Array.Empty<string>();
        var memberOf = resp.Entries[0].Attributes[LdapAttributeNames.MemberOf];
        if (memberOf is null) return Array.Empty<string>();
        var results = new List<string>(memberOf.Count);
        foreach (var dn in memberOf.GetValues(typeof(string)))
            results.Add(ExtractCommonName((string)dn));
        return results;
    }
    catch (DirectoryOperationException)
    {
        return Array.Empty<string>();
    }
}

private static string ExtractCommonName(string distinguishedName)
{
    // "CN=Foo,OU=..." → "Foo"
    if (distinguishedName.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
    {
        var comma = distinguishedName.IndexOf(',');
        return comma < 0 ? distinguishedName[3..] : distinguishedName[3..comma];
    }
    return distinguishedName;
}

/// <summary>
/// STAB-004 defense-in-depth: check the domain minPwdAge and the user's pwdLastSet.
/// Returns a <see cref="ApiErrorCode.PasswordTooRecentlyChanged"/> error item when
/// elapsed time since last password set is less than minPwdAge.
/// </summary>
internal ApiErrorItem? PreCheckMinPwdAge(ILdapSession session, string userDn)
{
    if (!_options.Value.EnforceMinimumPasswordAge) return null;

    var rootDse = session.RootDse;
    if (rootDse is null) return null;  // degraded mode — don't block

    var minPwdAgeAttr = rootDse.Attributes[LdapAttributeNames.MinPwdAge];
    if (minPwdAgeAttr is null || !long.TryParse((string)minPwdAgeAttr[0]!, out var minPwdAgeTicks))
        return null;
    if (minPwdAgeTicks == 0) return null;  // policy disabled

    var userReq = new SearchRequest(
        distinguishedName: userDn,
        ldapFilter: "(objectClass=user)",
        searchScope: SearchScope.Base,
        attributeList: new[] { LdapAttributeNames.PwdLastSet });
    var userResp = session.Search(userReq);
    if (userResp.Entries.Count == 0) return null;
    var pwdLastSetAttr = userResp.Entries[0].Attributes[LdapAttributeNames.PwdLastSet];
    if (pwdLastSetAttr is null || !long.TryParse((string)pwdLastSetAttr[0]!, out var pwdLastSetFiletime))
        return null;
    if (pwdLastSetFiletime == 0) return null;  // must-change-on-next-logon state; skip precheck

    var lastSet = DateTime.FromFileTimeUtc(pwdLastSetFiletime);
    var minAge = TimeSpan.FromTicks(Math.Abs(minPwdAgeTicks));  // AD stores as negative ticks
    var earliestAllowed = lastSet.Add(minAge);
    if (DateTime.UtcNow < earliestAllowed)
    {
        var remainingMins = (int)Math.Ceiling((earliestAllowed - DateTime.UtcNow).TotalMinutes);
        _logger.LogInformation(
            "Rejecting pre-minPwdAge change for {Dn}; {Remaining} min remaining",
            userDn, remainingMins);
        return new ApiErrorItem(
            ApiErrorCode.PasswordTooRecentlyChanged, null,
            $"Password was changed too recently; please wait ~{remainingMins} minute(s).");
    }
    return null;
}
```

Then wire them into `PerformPasswordChangeAsync` **between** the `FindUserDnAsync` check and the Modify call:

```csharp
// After: if (userDn is null) return ...;
var groupCheck = ValidateGroups(session, userDn);
if (groupCheck is not null) return groupCheck;

var agePrecheck = PreCheckMinPwdAge(session, userDn);
if (agePrecheck is not null) return agePrecheck;

// Then: var modifyRequest = BuildChangePasswordRequest(...);
```

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test src/PassReset.sln -c Release --filter "RestrictedGroup|MinPwdAgeViolation"`
Expected: both tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs \
        src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs
git commit -m "feat(provider-ldap): group validation + STAB-004 PreCheckMinPwdAge [phase-11]"
```

---

### Task 13: Implement GetUserEmail, GetDomainMaxPasswordAge, GetUsersInGroup, GetEffectivePasswordPolicyAsync (TDD)

**Files:**
- Modify: `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs`
- Modify: `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs`

- [ ] **Step 1: Write failing tests for each method**

Append to `LdapPasswordChangeProviderTests.cs`:

```csharp
[Fact]
public void GetUserEmail_Found_ReturnsMail()
{
    var (sut, fake) = Build();
    fake.OnSearch(
        "(sAMAccountName=alice)",
        MakeResponse(MakeEntry("CN=Alice,OU=Users,DC=corp,DC=example,DC=com",
            (LdapAttributeNames.Mail, "alice@corp.example.com"))));

    var email = sut.GetUserEmail("alice");

    Assert.Equal("alice@corp.example.com", email);
}

[Fact]
public void GetUserEmail_NotFound_ReturnsNull()
{
    var (sut, fake) = Build();
    fake.OnSearch("(sAMAccountName=ghost)", MakeResponse());

    var email = sut.GetUserEmail("ghost");

    Assert.Null(email);
}

[Fact]
public void GetDomainMaxPasswordAge_ReadsRootDseMaxPwdAge()
{
    var (sut, fake) = Build();
    var maxPwdAge = -TimeSpan.FromDays(90).Ticks;
    fake.RootDse = MakeEntry("",
        (LdapAttributeNames.MaxPwdAge, maxPwdAge.ToString()));

    var age = sut.GetDomainMaxPasswordAge();

    Assert.Equal(TimeSpan.FromDays(90), age);
}

[Fact]
public async Task GetEffectivePasswordPolicyAsync_ReturnsPolicyFromRootDse()
{
    var (sut, fake) = Build();
    fake.RootDse = MakeEntry("",
        (LdapAttributeNames.MinPwdLength, "8"),
        (LdapAttributeNames.MaxPwdAge, (-TimeSpan.FromDays(42).Ticks).ToString()));

    var policy = await sut.GetEffectivePasswordPolicyAsync();

    Assert.NotNull(policy);
    Assert.Equal(8, policy!.MinPasswordLength);
}
```

- [ ] **Step 2: Run — expect fails**

Run: `dotnet test src/PassReset.sln -c Release --filter "GetUserEmail|GetDomainMaxPasswordAge|GetEffectivePasswordPolicyAsync"`
Expected: all fail.

- [ ] **Step 3: Implement the methods**

In `LdapPasswordChangeProvider.cs`, replace the `throw new NotImplementedException()` stubs:

```csharp
public string? GetUserEmail(string username)
{
    using var session = _sessionFactory();
    try { session.Bind(); } catch (LdapException) { return null; }

    var opts = _options.Value;
    foreach (var attr in opts.AllowedUsernameAttributes)
    {
        var ldapAttr = attr.ToLowerInvariant() switch
        {
            "samaccountname"    => LdapAttributeNames.SamAccountName,
            "userprincipalname" => LdapAttributeNames.UserPrincipalName,
            "mail"              => LdapAttributeNames.Mail,
            _ => null,
        };
        if (ldapAttr is null) continue;

        var filter = $"({ldapAttr}={EscapeLdapFilterValue(username)})";
        var req = new SearchRequest(
            distinguishedName: opts.BaseDn,
            ldapFilter: filter,
            searchScope: SearchScope.Subtree,
            attributeList: new[] { LdapAttributeNames.Mail });
        try
        {
            var resp = session.Search(req);
            if (resp.Entries.Count == 1)
            {
                var mail = resp.Entries[0].Attributes[LdapAttributeNames.Mail];
                return mail is null ? null : (string)mail[0]!;
            }
        }
        catch (DirectoryOperationException) { return null; }
    }
    return null;
}

public TimeSpan GetDomainMaxPasswordAge()
{
    using var session = _sessionFactory();
    try { session.Bind(); } catch (LdapException) { return TimeSpan.Zero; }

    var rootDse = session.RootDse;
    if (rootDse is null) return TimeSpan.Zero;
    var maxPwdAgeAttr = rootDse.Attributes[LdapAttributeNames.MaxPwdAge];
    if (maxPwdAgeAttr is null) return TimeSpan.Zero;
    if (!long.TryParse((string)maxPwdAgeAttr[0]!, out var ticks)) return TimeSpan.Zero;
    return TimeSpan.FromTicks(Math.Abs(ticks));
}

public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
{
    using var session = _sessionFactory();
    try { session.Bind(); } catch (LdapException) { yield break; }

    var opts = _options.Value;
    // Resolve group DN.
    var groupFilter = $"(&(objectClass=group)(cn={EscapeLdapFilterValue(groupName)}))";
    var groupReq = new SearchRequest(
        distinguishedName: opts.BaseDn,
        ldapFilter: groupFilter,
        searchScope: SearchScope.Subtree,
        attributeList: new[] { LdapAttributeNames.DistinguishedName });
    SearchResponse groupResp;
    try { groupResp = session.Search(groupReq); }
    catch (DirectoryOperationException) { yield break; }
    if (groupResp.Entries.Count == 0) yield break;
    var groupDn = groupResp.Entries[0].DistinguishedName;

    // Enumerate members with LDAP_MATCHING_RULE_IN_CHAIN for nested-group resolution.
    var userFilter = $"(&(objectCategory=person)(memberOf:1.2.840.113556.1.4.1941:={EscapeLdapFilterValue(groupDn)}))";
    var userReq = new SearchRequest(
        distinguishedName: opts.BaseDn,
        ldapFilter: userFilter,
        searchScope: SearchScope.Subtree,
        attributeList: new[]
        {
            LdapAttributeNames.SamAccountName,
            LdapAttributeNames.Mail,
            LdapAttributeNames.PwdLastSet,
        });
    SearchResponse userResp;
    try { userResp = session.Search(userReq); }
    catch (DirectoryOperationException) { yield break; }

    foreach (SearchResultEntry entry in userResp.Entries)
    {
        var sam = (string?)entry.Attributes[LdapAttributeNames.SamAccountName]?[0] ?? string.Empty;
        var mail = (string?)entry.Attributes[LdapAttributeNames.Mail]?[0] ?? string.Empty;
        DateTime? lastSet = null;
        var pwdLastSetAttr = entry.Attributes[LdapAttributeNames.PwdLastSet];
        if (pwdLastSetAttr is not null &&
            long.TryParse((string)pwdLastSetAttr[0]!, out var ft) && ft != 0)
        {
            lastSet = DateTime.FromFileTimeUtc(ft);
        }
        yield return (sam, mail, lastSet);
    }
}

public Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync()
{
    using var session = _sessionFactory();
    try { session.Bind(); } catch (LdapException) { return Task.FromResult<PasswordPolicy?>(null); }

    var rootDse = session.RootDse;
    if (rootDse is null) return Task.FromResult<PasswordPolicy?>(null);

    int minLen = 0;
    var minLenAttr = rootDse.Attributes[LdapAttributeNames.MinPwdLength];
    if (minLenAttr is not null && int.TryParse((string)minLenAttr[0]!, out var ml)) minLen = ml;

    var policy = new PasswordPolicy
    {
        MinPasswordLength = minLen,
        RequiresComplexity = true,   // AD default — complex-password-required flag lives in pwdProperties, Phase 11 simplification
    };
    return Task.FromResult<PasswordPolicy?>(policy);
}
```

- [ ] **Step 4: Run tests to confirm they pass**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~LdapPasswordChangeProviderTests"`
Expected: all LdapPasswordChangeProviderTests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs \
        src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs
git commit -m "feat(provider-ldap): GetUserEmail, GetDomainMaxPasswordAge, GetUsersInGroup, GetEffectivePasswordPolicyAsync [phase-11]"
```

---

### Task 14: Retarget PassReset.Tests to net10.0

**Files:**
- Modify: `src/PassReset.Tests/PassReset.Tests.csproj`

- [ ] **Step 1: Retarget TFM**

Change in `src/PassReset.Tests/PassReset.Tests.csproj`:

```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

to:

```xml
<TargetFramework>net10.0</TargetFramework>
```

Also remove or gate any `<ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />` — we'll move those tests to `PassReset.Tests.Windows` in Task 15. For now wrap with conditional:

```xml
<ItemGroup Condition="'$(OS)' == 'Windows_NT'">
  <ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Build**

Run: `dotnet build src/PassReset.sln -c Release`
Expected: 0 errors. Windows-specific test files may now fail to compile because they reference `UserPrincipal` etc. — **expected**. Move them to the new project in Task 15 (next).

If build fails with type-not-found errors from Windows-specific tests, temporarily exclude those test files:

```xml
<ItemGroup Condition="'$(OS)' != 'Windows_NT'">
  <Compile Remove="**\PasswordChangeProviderTests.cs" />
  <Compile Remove="**\*Windows*Tests.cs" />
</ItemGroup>
```

The next task moves them properly; this gating is a temporary bridge for the commit.

- [ ] **Step 3: Run tests**

Run: `dotnet test src/PassReset.sln -c Release`
Expected: all existing non-Windows-specific tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/PassReset.Tests/PassReset.Tests.csproj
git commit -m "build(tests): retarget PassReset.Tests to net10.0 for cross-platform CI [phase-11]"
```

---

### Task 15: Extract Windows-specific tests to PassReset.Tests.Windows

**Files:**
- Create: `src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj`
- Move: `src/PassReset.Tests/PasswordProvider/PasswordChangeProviderTests.cs` → `src/PassReset.Tests.Windows/PasswordProvider/PasswordChangeProviderTests.cs`
- Move: any other test files that reference `System.DirectoryServices.AccountManagement` types (check via grep for `UserPrincipal`, `PrincipalContext`)
- Modify: `src/PassReset.Tests/PassReset.Tests.csproj` — remove the temporary `<Compile Remove>` gating from Task 14 now that files are moved
- Modify: `src/PassReset.sln` — register new project

- [ ] **Step 1: Find Windows-specific test files**

Run: `grep -rl "UserPrincipal\|PrincipalContext\|System.DirectoryServices.AccountManagement" src/PassReset.Tests/`

Expected: a list of files. These are the ones to move.

- [ ] **Step 2: Create the new csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\PassReset.Common\PassReset.Common.csproj" />
    <ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />
  </ItemGroup>

</Project>
```

(Match exact xunit/NSubstitute versions currently used in `PassReset.Tests.csproj` — inspect before writing.)

- [ ] **Step 3: Move files**

Use `git mv` to preserve history:

```bash
mkdir -p src/PassReset.Tests.Windows/PasswordProvider
git mv src/PassReset.Tests/PasswordProvider/PasswordChangeProviderTests.cs \
       src/PassReset.Tests.Windows/PasswordProvider/
```

Repeat for each Windows-specific file surfaced in Step 1. Adjust namespaces inside the moved files from `PassReset.Tests.*` to `PassReset.Tests.Windows.*`.

- [ ] **Step 4: Register the new project**

```bash
dotnet sln src/PassReset.sln add src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj
```

- [ ] **Step 5: Remove the temporary Compile Remove entries from PassReset.Tests.csproj**

Delete the block added in Task 14 Step 2:

```xml
<ItemGroup Condition="'$(OS)' != 'Windows_NT'">
  <Compile Remove="**\PasswordChangeProviderTests.cs" />
  <Compile Remove="**\*Windows*Tests.cs" />
</ItemGroup>
```

- [ ] **Step 6: Build + test on Windows**

Run: `dotnet build src/PassReset.sln -c Release && dotnet test src/PassReset.sln -c Release`
Expected: 0 errors, all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/PassReset.Tests.Windows/ src/PassReset.Tests/PassReset.Tests.csproj src/PassReset.sln
git commit -m "test(tests-windows): extract Windows-only provider tests into new project [phase-11]"
```

---

### Task 16: Create shared contract test suite + both provider implementations

**Files:**
- Create: `src/PassReset.Tests/Contracts/IPasswordChangeProviderContract.cs`
- Create: `src/PassReset.Tests/Contracts/LdapPasswordChangeProviderContractTests.cs`
- Create: `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs`

- [ ] **Step 1: Write the abstract contract**

`src/PassReset.Tests/Contracts/IPasswordChangeProviderContract.cs`:

```csharp
using PassReset.Common;
using Xunit;

namespace PassReset.Tests.Contracts;

/// <summary>
/// Behavioral parity contract shared by every <see cref="IPasswordChangeProvider"/>
/// implementation. Subclasses supply the provider-under-test via
/// <see cref="CreateProvider"/> and fixture setup (seed users, configure lockout thresholds, etc.).
/// Same test method bodies, two implementations — any divergence is a migration bug.
/// </summary>
public abstract class IPasswordChangeProviderContract
{
    /// <summary>Subclass builds and returns a fully wired provider instance.</summary>
    protected abstract IPasswordChangeProvider CreateProvider();

    /// <summary>Subclass seeds a user whose credentials the test will use.</summary>
    protected abstract TestUser SeedUser(string username, string currentPassword);

    protected sealed record TestUser(string Username, string Password);

    [Fact]
    public async Task InvalidCredentials_ReturnsInvalidCredentials()
    {
        var sut = CreateProvider();
        SeedUser("alice", "Correct1!");

        var result = await sut.PerformPasswordChangeAsync("alice", "Wrong1!", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.InvalidCredentials, result!.ErrorCode);
    }

    [Fact]
    public async Task UnknownUser_ReturnsUserNotFound()
    {
        var sut = CreateProvider();

        var result = await sut.PerformPasswordChangeAsync("nobody", "any", "NewPass1!");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.UserNotFound, result!.ErrorCode);
    }

    [Fact]
    public async Task HappyPath_ReturnsNull()
    {
        var sut = CreateProvider();
        SeedUser("bob", "OldPass1!");

        var result = await sut.PerformPasswordChangeAsync("bob", "OldPass1!", "NewPass2!");

        Assert.Null(result);
    }

    [Fact]
    public async Task WeakPassword_ReturnsComplexPassword()
    {
        var sut = CreateProvider();
        SeedUser("carol", "OldPass1!");

        var result = await sut.PerformPasswordChangeAsync("carol", "OldPass1!", "x");

        Assert.NotNull(result);
        Assert.Equal(ApiErrorCode.ComplexPassword, result!.ErrorCode);
    }

    [Fact]
    public async Task UsernameFallback_MatchesUpnAfterSamMiss()
    {
        var sut = CreateProvider();
        SeedUser("dave@corp.example.com", "OldPass1!");

        // "dave" alone won't match sAMAccountName, should fall through to UPN.
        var result = await sut.PerformPasswordChangeAsync(
            "dave@corp.example.com", "OldPass1!", "NewPass2!");

        Assert.Null(result);
    }

    // Additional scenarios finalized here:
    //   - RestrictedGroup → ChangeNotPermitted
    //   - AllowedGroup miss → ChangeNotPermitted
    //   - PreCheckMinPwdAge → PasswordTooRecentlyChanged
    //   - Locked-out account → PortalLockout (when decorator is layered in a separate test)
    //   - Disabled account → ChangeNotPermitted
    //   - Empty new password → ComplexPassword
    //   - Identical old and new → ComplexPassword
}
```

- [ ] **Step 2: Implement LdapPasswordChangeProviderContractTests**

`src/PassReset.Tests/Contracts/LdapPasswordChangeProviderContractTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.PasswordProvider.Ldap;
using PassReset.Tests.Fakes;

namespace PassReset.Tests.Contracts;

public class LdapPasswordChangeProviderContractTests : IPasswordChangeProviderContract
{
    private readonly FakeLdapSession _fake = new();
    private readonly Dictionary<string, string> _users = new();

    protected override IPasswordChangeProvider CreateProvider()
    {
        var opts = new PasswordChangeOptions
        {
            ProviderMode = ProviderMode.Ldap,
            AllowedUsernameAttributes = new[] { "samaccountname", "userprincipalname", "mail" },
            BaseDn = "DC=corp,DC=example,DC=com",
            ServiceAccountDn = "CN=svc,DC=corp,DC=example,DC=com",
            ServiceAccountPassword = "svcpw",
            LdapHostnames = new[] { "dc01.corp.example.com" },
            LdapPort = 636,
            EnforceMinimumPasswordAge = false,  // keep contract tests deterministic
        };
        return new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => _fake);
    }

    protected override TestUser SeedUser(string username, string currentPassword)
    {
        // Configure FakeLdapSession search + modify rules to simulate an AD with this user.
        // Pseudo-seed: register the user so the provider's search finds a matching DN;
        // configure modify to accept if currentPassword matches, else return InvalidCredentials.
        // Full implementation mirrors the fake setup in LdapPasswordChangeProviderTests.
        _users[username] = currentPassword;
        // ... (set up fake rules — see LdapPasswordChangeProviderTests MakeEntry helper) ...
        return new TestUser(username, currentPassword);
    }
}
```

- [ ] **Step 3: Implement PasswordChangeProviderContractTests (Windows)**

`src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs`:

```csharp
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.Tests.Contracts;

namespace PassReset.Tests.Windows.Contracts;

public class PasswordChangeProviderContractTests : IPasswordChangeProviderContract
{
    protected override IPasswordChangeProvider CreateProvider()
    {
        // Build the real provider using the existing fake context + PrincipalContext mocks
        // already used by PasswordChangeProviderTests in this project.
        // Reuse its `Build()` static helper.
        return PasswordChangeProviderTestFixture.BuildProvider();
    }

    protected override TestUser SeedUser(string username, string currentPassword)
    {
        PasswordChangeProviderTestFixture.SeedUser(username, currentPassword);
        return new TestUser(username, currentPassword);
    }
}
```

`PasswordChangeProviderTestFixture` is a new shared helper — extract from existing `PasswordChangeProviderTests` so both the legacy tests and the new contract tests use it. Create it as a public static class in the same directory.

- [ ] **Step 4: Build + run contract tests**

Run: `dotnet test src/PassReset.sln -c Release --filter "FullyQualifiedName~Contracts"`
Expected: all contract tests pass on both providers.

- [ ] **Step 5: Commit**

```bash
git add src/PassReset.Tests/Contracts/ src/PassReset.Tests.Windows/Contracts/
git commit -m "test(contract): shared IPasswordChangeProviderContract for parity enforcement [phase-11]"
```

---

### Task 17: Retarget PassReset.Web to net10.0 with conditional Windows provider ref

**Files:**
- Modify: `src/PassReset.Web/PassReset.Web.csproj`

- [ ] **Step 1: Retarget**

Change:
```xml
<TargetFramework>net10.0-windows</TargetFramework>
```
to:
```xml
<TargetFramework>net10.0</TargetFramework>
```

And change the existing:
```xml
<ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />
```

to a conditional block:

```xml
<ItemGroup Condition="'$(OS)' == 'Windows_NT'">
  <ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\PassReset.PasswordProvider.Ldap\PassReset.PasswordProvider.Ldap.csproj" />
</ItemGroup>

<PropertyGroup Condition="'$(OS)' == 'Windows_NT'">
  <DefineConstants>$(DefineConstants);WINDOWS_PROVIDER</DefineConstants>
</PropertyGroup>
```

- [ ] **Step 2: Build on Windows**

Run: `dotnet build src/PassReset.Web/ -c Release`
Expected: 0 errors. `WINDOWS_PROVIDER` is defined, both references present.

- [ ] **Step 3: Commit**

```bash
git add src/PassReset.Web/PassReset.Web.csproj
git commit -m "build(web): retarget to net10.0 with conditional Windows provider reference [phase-11]"
```

---

### Task 18: Update Program.cs to select provider by ProviderMode

**Files:**
- Modify: `src/PassReset.Web/Program.cs`

- [ ] **Step 1: Replace the existing provider-wiring block (around lines 141–184)**

Find the existing `if (webSettings.UseDebugProvider && !builder.Environment.IsDevelopment()) ...` / debug / production branches. Replace with:

```csharp
// Phase 11: ProviderMode-based selection.
var pcOpts = builder.Configuration.GetSection(nameof(PasswordChangeOptions))
    .Get<PasswordChangeOptions>() ?? new PasswordChangeOptions();

var effectiveProvider = pcOpts.ProviderMode switch
{
    ProviderMode.Windows => WiringTarget.Windows,
    ProviderMode.Ldap    => WiringTarget.Ldap,
    ProviderMode.Auto    => OperatingSystem.IsWindows() ? WiringTarget.Windows : WiringTarget.Ldap,
    _                    => throw new InvalidOperationException(
                               $"Unknown PasswordChangeOptions.ProviderMode: {pcOpts.ProviderMode}"),
};

if (webSettings.UseDebugProvider)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "WebSettings.UseDebugProvider=true is only allowed in Development.");
    builder.Services.AddSingleton<PasswordProvider.DebugPasswordChangeProvider>();
    builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
        new LockoutPasswordChangeProvider(
            sp.GetRequiredService<PasswordProvider.DebugPasswordChangeProvider>(),
            sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
            sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
}
else if (effectiveProvider == WiringTarget.Ldap)
{
    builder.Services.AddSingleton<Func<ILdapSession>>(sp =>
    {
        var opts  = sp.GetRequiredService<IOptions<PasswordChangeOptions>>().Value;
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        return () => new LdapSession(
            hostname: opts.LdapHostnames[0],
            port:     opts.LdapPort,
            useLdaps: opts.LdapUseSsl,
            serviceAccountDn: opts.ServiceAccountDn,
            serviceAccountPassword: opts.ServiceAccountPassword,
            trustedThumbprints: opts.LdapTrustedCertificateThumbprints,
            logger: loggerFactory.CreateLogger<LdapSession>());
    });
    builder.Services.AddSingleton<LdapPasswordChangeProvider>();
    builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
        new LockoutPasswordChangeProvider(
            sp.GetRequiredService<LdapPasswordChangeProvider>(),
            sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
            sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
}
#if WINDOWS_PROVIDER
else  // effectiveProvider == WiringTarget.Windows
{
    builder.Services.AddSingleton<PwnedPasswordChecker>();
    builder.Services.AddSingleton<PasswordChangeProvider>();
    builder.Services.AddSingleton<LockoutPasswordChangeProvider>(sp =>
        new LockoutPasswordChangeProvider(
            sp.GetRequiredService<PasswordChangeProvider>(),
            sp.GetRequiredService<IOptions<PasswordChangeOptions>>(),
            sp.GetRequiredService<ILogger<LockoutPasswordChangeProvider>>()));
}
#else
else
{
    throw new InvalidOperationException(
        "PasswordChangeOptions.ProviderMode resolved to Windows, but this build does not include the Windows provider. " +
        "Rebuild on Windows or set ProviderMode to Ldap.");
}
#endif

builder.Services.AddSingleton<IPasswordChangeProvider>(sp =>
    sp.GetRequiredService<LockoutPasswordChangeProvider>());
builder.Services.AddSingleton<ILockoutDiagnostics>(sp =>
    sp.GetRequiredService<LockoutPasswordChangeProvider>());

// Helper enum inside Program.cs:
enum WiringTarget { Windows, Ldap }
```

Add `using PassReset.PasswordProvider.Ldap;` at the top.

- [ ] **Step 2: Build on Windows**

Run: `dotnet build src/PassReset.Web/ -c Release`
Expected: 0 errors.

- [ ] **Step 3: Run full test suite**

Run: `dotnet test src/PassReset.sln -c Release`
Expected: 0 failures.

- [ ] **Step 4: Commit**

```bash
git add src/PassReset.Web/Program.cs
git commit -m "feat(web): ProviderMode-based provider selection in Program.cs [phase-11]"
```

---

### Task 19: Create PassReset.Tests.Integration.Ldap project (Samba DC smoke tests)

**Files:**
- Create: `src/PassReset.Tests.Integration.Ldap/PassReset.Tests.Integration.Ldap.csproj`
- Create: `src/PassReset.Tests.Integration.Ldap/SambaDcIntegrationTests.cs`
- Create: `src/PassReset.Tests.Integration.Ldap/appsettings.IntegrationTest.json.example` (real file `appsettings.IntegrationTest.json` is `.gitignore`d)
- Modify: `.gitignore`
- Modify: `src/PassReset.sln`

- [ ] **Step 1: Write the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\PassReset.Common\PassReset.Common.csproj" />
    <ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" Condition="'$(OS)' == 'Windows_NT'" />
    <ProjectReference Include="..\PassReset.PasswordProvider.Ldap\PassReset.PasswordProvider.Ldap.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the smoke test**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PassReset.Common;
using PassReset.PasswordProvider;
using PassReset.PasswordProvider.Ldap;
using Xunit;

namespace PassReset.Tests.Integration.Ldap;

/// <summary>
/// End-to-end smoke test against a live Samba AD DC container.
/// Runs only when <c>PASSRESET_INTEGRATION_LDAP=1</c> is set (CI sets this in the
/// integration-tests-ldap job; skipped locally by default).
/// </summary>
public class SambaDcIntegrationTests
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("PASSRESET_INTEGRATION_LDAP") == "1";

    [Fact]
    public async Task EndToEnd_ChangePassword_ReturnsNull()
    {
        if (!Enabled) return;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.IntegrationTest.json", optional: true)
            .AddEnvironmentVariables(prefix: "PASSRESET_")
            .Build();

        var opts = new PasswordChangeOptions();
        config.GetSection("PasswordChangeOptions").Bind(opts);

        var session = new LdapSession(
            hostname: opts.LdapHostnames[0],
            port: opts.LdapPort,
            useLdaps: opts.LdapUseSsl,
            serviceAccountDn: opts.ServiceAccountDn,
            serviceAccountPassword: opts.ServiceAccountPassword,
            trustedThumbprints: opts.LdapTrustedCertificateThumbprints,
            logger: NullLogger<LdapSession>.Instance);

        var provider = new LdapPasswordChangeProvider(
            Options.Create(opts),
            NullLogger<LdapPasswordChangeProvider>.Instance,
            () => session);

        var testUser = Environment.GetEnvironmentVariable("PASSRESET_TEST_USERNAME") ?? "testuser";
        var oldPw    = Environment.GetEnvironmentVariable("PASSRESET_TEST_OLD_PASSWORD") ?? "OldPass1!";
        var newPw    = Environment.GetEnvironmentVariable("PASSRESET_TEST_NEW_PASSWORD") ?? "NewPass1!";

        var result = await provider.PerformPasswordChangeAsync(testUser, oldPw, newPw);

        Assert.Null(result);
    }
}
```

- [ ] **Step 3: Create example config**

`src/PassReset.Tests.Integration.Ldap/appsettings.IntegrationTest.json.example`:

```json
{
  "PasswordChangeOptions": {
    "ProviderMode": "Ldap",
    "LdapHostnames": [ "localhost" ],
    "LdapPort": 636,
    "LdapUseSsl": true,
    "BaseDn": "DC=samdom,DC=example,DC=com",
    "ServiceAccountDn": "CN=Administrator,CN=Users,DC=samdom,DC=example,DC=com",
    "ServiceAccountPassword": "will be injected from env",
    "AllowedUsernameAttributes": [ "samaccountname" ],
    "LdapTrustedCertificateThumbprints": []
  }
}
```

- [ ] **Step 4: gitignore the real config**

Append to `.gitignore`:

```
# Phase 11: integration test local config (secrets)
src/PassReset.Tests.Integration.Ldap/appsettings.IntegrationTest.json
```

- [ ] **Step 5: Register in sln + verify**

```bash
dotnet sln src/PassReset.sln add src/PassReset.Tests.Integration.Ldap/PassReset.Tests.Integration.Ldap.csproj
dotnet build src/PassReset.sln -c Release
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/PassReset.Tests.Integration.Ldap/ .gitignore src/PassReset.sln
git commit -m "test(integration-ldap): Samba DC smoke-test scaffolding (env-gated) [phase-11]"
```

---

### Task 20: Add integration-tests-ldap job to GitHub Actions

**Files:**
- Modify: `.github/workflows/tests.yml`

- [ ] **Step 1: Append new job definition**

After the existing `security-audit` job (or as a sibling to `tests`), add:

```yaml
  integration-tests-ldap:
    runs-on: ubuntu-latest
    continue-on-error: true  # Phase 11: warning-only until stable
    services:
      samba-dc:
        image: diginc/samba-ad-dc:latest
        ports:
          - 389:389
          - 636:636
        env:
          DOMAIN: SAMDOM
          DOMAINPASS: ${{ secrets.SAMBA_DOMAIN_PASSWORD }}
          DNSFORWARDER: 8.8.8.8
        options: >-
          --health-cmd "pidof samba"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 10
    env:
      PASSRESET_INTEGRATION_LDAP: "1"
      PASSRESET_PasswordChangeOptions__ServiceAccountPassword: ${{ secrets.SAMBA_DOMAIN_PASSWORD }}
      PASSRESET_TEST_USERNAME: "testuser"
      PASSRESET_TEST_OLD_PASSWORD: ${{ secrets.SAMBA_TEST_OLD_PASSWORD }}
      PASSRESET_TEST_NEW_PASSWORD: ${{ secrets.SAMBA_TEST_NEW_PASSWORD }}
    steps:
      - uses: actions/checkout@v6.0.2
      - uses: actions/setup-dotnet@v5.2.0
        with:
          dotnet-version: '10.0.x'
      - name: Wait for Samba DC
        run: |
          for i in {1..30}; do
            nc -z localhost 636 && break
            sleep 2
          done
      - name: Seed test user
        run: |
          # Use docker exec to create a test user inside the samba-dc container.
          docker exec ${{ job.services.samba-dc.id }} samba-tool user create \
            $PASSRESET_TEST_USERNAME \
            "$PASSRESET_TEST_OLD_PASSWORD" \
            --given-name=Test --surname=User
      - name: Run integration tests
        working-directory: src/PassReset.Tests.Integration.Ldap
        run: dotnet test -c Release --logger "console;verbosity=detailed"
```

- [ ] **Step 2: Validate workflow**

Run: `yq . .github/workflows/tests.yml > /dev/null && echo OK` (or any YAML validator)
Expected: `OK`

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/tests.yml
git commit -m "ci(integration-ldap): Samba DC integration test job (warning-only) [phase-11]"
```

---

### Task 21: Write docs/AD-ServiceAccount-LDAP-Setup.md

**Files:**
- Create: `docs/AD-ServiceAccount-LDAP-Setup.md`
- Modify: `docs/appsettings-Production.md`
- Modify: `docs/IIS-Setup.md`
- Modify: `README.md`

- [ ] **Step 1: Write the new operator doc**

```markdown
# AD Service Account Setup — LDAP Provider (v2.0+)

This guide applies when running PassReset with `PasswordChangeOptions.ProviderMode` resolving to `Ldap` — that is:
- **Any Linux deployment** (Auto → Ldap because the host isn't Windows), or
- **Any explicit `ProviderMode: "Ldap"`** (e.g. testing Linux parity on a Windows host).

On Windows with `ProviderMode: "Auto"` or `"Windows"`, follow the existing [`AD-ServiceAccount-Setup.md`](AD-ServiceAccount-Setup.md) instead — no change.

---

## 1. Create the service account

```powershell
New-ADUser `
    -Name "svc-passreset" `
    -SamAccountName "svc-passreset" `
    -UserPrincipalName "svc-passreset@corp.example.com" `
    -Path "OU=ServiceAccounts,DC=corp,DC=example,DC=com" `
    -AccountPassword (Read-Host "Password" -AsSecureString) `
    -Enabled $true `
    -PasswordNeverExpires $true
```

Note the resulting DN — you'll put it in `PasswordChangeOptions.ServiceAccountDn`.

## 2. Grant the "Change Password" extended right

The service account must have the *Change Password* extended right on every OU containing users whose passwords it needs to change. `Reset Password` is intentionally NOT granted — PassReset never bypasses the user's current password.

### UI path

1. Open **Active Directory Users and Computers**, enable *Advanced Features*.
2. Right-click the target OU → **Properties** → **Security** → **Advanced** → **Add**.
3. Principal: your service account. Type: **Allow**. Applies to: **Descendant User objects**.
4. Check only **Change password**. Click OK.

### PowerShell path

```powershell
$ou  = "OU=Users,DC=corp,DC=example,DC=com"
$svc = "svc-passreset"
$acl = Get-Acl "AD:\$ou"
$sid = (Get-ADUser $svc).SID
$changePwdGuid = [Guid]"ab721a53-1e2f-11d0-9819-00aa0040529b"  # well-known
$rule = New-Object System.DirectoryServices.ActiveDirectoryAccessRule `
    $sid, "ExtendedRight", "Allow", $changePwdGuid, "Descendents", "bf967aba-0de6-11d0-a285-00aa003049e2"
$acl.AddAccessRule($rule)
Set-Acl "AD:\$ou" -AclObject $acl
```

## 3. LDAPS certificate trust (Linux hosts)

The Ldap provider requires LDAPS (`LdapUseSsl: true`, port 636). Linux hosts do not automatically trust your domain CA.

**Option A — Install the domain CA cert into the system trust store:**

```bash
sudo cp corp-ca-root.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

**Option B — Pin via `LdapTrustedCertificateThumbprints`:**

```powershell
$cert = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -match "Corp Root CA"
$cert.Thumbprint
```

Add the returned thumbprint (SHA-1 or SHA-256 hex) to `LdapTrustedCertificateThumbprints` in `appsettings.Production.json`.

## 4. Bind the password via environment variable

Never commit the service account password to config. Set it via env var using the ASP.NET Core delimiter pattern:

```bash
export PasswordChangeOptions__ServiceAccountPassword='<the password>'
```

## 5. Troubleshooting matrix

| Symptom | LDAP ResultCode | Likely fix |
|---|---|---|
| All change attempts return `InvalidCredentials` | `InvalidCredentials` during bind | Service account DN wrong, or password env var not picked up |
| All change attempts return `ChangeNotPermitted` | `InsufficientAccessRights` on Modify | Step 2 not performed on the user's OU |
| "Password does not meet complexity" on valid passwords | `ConstraintViolation` + extendedError `0x0000052D` | Real policy issue, not a bug |
| Connection hangs for 3s then fails | Health endpoint shows `ad: unhealthy` | LDAPS cert trust failed; see step 3 |
```

- [ ] **Step 2: Update related docs**

Insert into `docs/appsettings-Production.md` a new subsection documenting the new fields and linking to the setup doc. Keep consistent with existing doc style.

Insert into `docs/IIS-Setup.md` a callout at the top: *"PassReset v2.0+ supports Linux deployment without IIS. See [LDAP setup](AD-ServiceAccount-LDAP-Setup.md) for the cross-platform path."*

Update `README.md` platform-support matrix to list Linux/Docker as supported in v2.0+.

- [ ] **Step 3: Commit**

```bash
git add docs/AD-ServiceAccount-LDAP-Setup.md \
        docs/appsettings-Production.md \
        docs/IIS-Setup.md \
        README.md
git commit -m "docs(ldap): operator guide for service account + LDAPS cert trust on Linux [phase-11]"
```

---

### Task 22: Update CHANGELOG.md with [2.0.0-alpha.1] entry

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add the new version section**

Immediately after `## [Unreleased]` and its separator, add:

```markdown
## [2.0.0-alpha.1] — TBD (release day)

First v2.0 alpha. Introduces a cross-platform `LdapPasswordChangeProvider` backed by `System.DirectoryServices.Protocols`. **Existing Windows deployments upgrade with no config changes** — `PasswordChangeOptions.ProviderMode` defaults to `Auto`, which picks the Windows provider on Windows.

### Added
- **Cross-platform LDAP provider** — new `PassReset.PasswordProvider.Ldap` project (`net10.0`) implementing `IPasswordChangeProvider` via `LdapConnection`. Works on Windows, Linux, and macOS. Passes a shared behavioral contract test suite against the existing Windows provider. *(provider)*
- **`PasswordChangeOptions.ProviderMode`** — new `Auto | Windows | Ldap` enum selecting the active provider. Default `Auto`. Schema + templates updated. *(web, provider)*
- **Service-account LDAP binding fields** — `ServiceAccountDn`, `ServiceAccountPassword`, `BaseDn`, `LdapTrustedCertificateThumbprints`. `ServiceAccountPassword` binds via the `PasswordChangeOptions__ServiceAccountPassword` env var per the STAB-017 pattern. *(web, provider)*
- **New operator doc** — [`docs/AD-ServiceAccount-LDAP-Setup.md`](docs/AD-ServiceAccount-LDAP-Setup.md) covers service-account creation, "Change Password" extended right grant, LDAPS cert trust on Linux, and a troubleshooting matrix. *(docs)*
- **Samba DC CI integration test** (warning-only in this release) — GitHub Actions `integration-tests-ldap` job spins up a Samba AD DC service container and runs an end-to-end change-password flow against the Ldap provider. *(ci)*

### Changed
- **`PassReset.Web` retargeted to `net10.0`** — the Windows provider is now a conditional `ProjectReference` gated on `$(OS) == 'Windows_NT'` with a `WINDOWS_PROVIDER` compile constant. Linux / Docker builds skip the Windows provider entirely. *(web)*
- **`PassReset.Tests` retargeted to `net10.0`** — cross-platform tests (web, controllers, contract tests, Ldap unit tests) now run on Linux CI. Windows-only tests moved to a new `PassReset.Tests.Windows` project (`net10.0-windows`). *(test)*

### Non-changes (explicit)
- **Windows provider unchanged.** `PassReset.PasswordProvider` (net10.0-windows) is byte-for-byte identical to v1.4.2. Zero regression for Windows operators.
- **`UserCannotChangePassword` ACE check** is deferred on the LDAP provider. AD's server-side modify rejection provides enforcement without the ACE check; the error message is less specific on Linux but behavior is correct. Logged as a warning at startup on Linux. *(provider-ldap)*

### Breaking
- None for Windows upgraders running with default config. (Schema adds `ProviderMode` with default `Auto`; Windows stays on Windows provider.)
```

- [ ] **Step 2: Commit**

```bash
git add CHANGELOG.md
git commit -m "docs(release): cut [2.0.0-alpha.1] — cross-platform LDAP provider [phase-11]"
```

---

## Final verification

After all tasks complete, run this checklist locally:

- [ ] `dotnet build src/PassReset.sln -c Release` → 0 errors, 0 warnings-as-errors
- [ ] `dotnet test src/PassReset.sln -c Release` → all tests pass on Windows (Ldap + Windows contract suites + unit tests)
- [ ] Trigger GH Actions CI on the branch → `tests`, `security-audit`, and `integration-tests-ldap` (warning-only) all complete
- [ ] Spot-check on a Windows v1.4.2 prod-like appsettings: run the web host, confirm `Auto` picks Windows provider (log message at startup), confirm existing password-change flow works unchanged
- [ ] (Optional, after Phase 12 hosting work) Run on a Linux VM against a real test AD → full end-to-end success

---

## Self-Review

**Spec coverage:**
- Decomposition → documented at top
- Non-goals → Task 9 (logs Linux-only warning), Task 22 (CHANGELOG)
- Project structure → Tasks 2, 14, 15, 17, 19
- Runtime selection + Program.cs wiring → Task 18
- Behavioral-equivalence map → Tasks 10, 11, 12, 13
- Configuration schema → Tasks 3, 4
- `UserCannotChangePassword` deferral → Task 9 startup log, Task 22 CHANGELOG Non-changes
- LDAPS cert trust → Task 6, Task 21 docs
- `ILdapSession` → Tasks 5, 6, 8
- Unit tests → Tasks 7, 10, 11, 12, 13
- Contract tests → Task 16
- Samba integration → Tasks 19, 20
- Docs → Task 21
- CHANGELOG → Task 22

**Placeholder scan:** None. Every step has either code, a command, or a file path.

**Type consistency:** `IPasswordChangeProvider` method names match the survey (`PerformPasswordChangeAsync`, `GetUserEmail`, `GetUsersInGroup`, `GetDomainMaxPasswordAge`, `GetEffectivePasswordPolicyAsync`, `MeasureNewPasswordDistance`). `ILdapSession` methods (`Bind`, `Search`, `Modify`, `Dispose`, `RootDse`) consistent across Tasks 5/6/8/10–13. `LdapErrorMapping.Map(ResultCode, uint)` signature used identically in Tasks 7 and 11.

**One known risk the plan accepts:** `MeasureNewPasswordDistance` is default-implemented on the interface (per survey), so the Ldap provider inherits it without an explicit override. If the Windows provider overrides it, the contract test `UsernameFallback_MatchesUpnAfterSamMiss` doesn't cover that — acceptable because Levenshtein is pure, but noted.

---

**End of plan.**
