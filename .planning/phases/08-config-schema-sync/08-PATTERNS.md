# Phase 8: Configuration Schema & Sync - Pattern Map

**Mapped:** 2026-04-16
**Files analyzed:** 7 (1 NEW + 6 modified)
**Analogs found:** 6 / 7 (the new JSON Schema file has no in-repo analog)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/PassReset.Web/appsettings.schema.json` (NEW) | config-schema | static-asset | `src/PassReset.Web/appsettings.Production.template.json` (sibling artifact, same publish lifecycle) | partial — no JSON Schema exists; sibling defines key structure |
| `src/PassReset.Web/appsettings.Production.template.json` (modify — strip comments) | config-template | static-asset | `src/PassReset.Web/appsettings.json` (uses JSONC; template currently mirrors that style) | exact (sibling file) |
| `src/PassReset.Web/Program.cs` (modify — `.Validate(...).ValidateOnStart()`) | composition-root / DI bootstrap | startup-validation | `src/PassReset.Web/Program.cs` lines 33-47 (existing `Configure<T>` block + the existing `AddSingleton<IValidateOptions<PasswordChangeOptions>, …>`) | exact (extending existing pattern) |
| `src/PassReset.Web/Models/*.cs` validators (NEW sibling validator classes per options class) | options-validator | startup-validation | `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs` | exact |
| `deploy/Install-PassReset.ps1` — `Test-Json` pre-flight | installer / pre-flight | request-response (script-step) | `deploy/Publish-PassReset.ps1:69-93` (existing template-vs-appsettings drift validator) | role-match |
| `deploy/Install-PassReset.ps1` — `-ConfigSync` param + interactive prompt | installer / parameter binding + prompt | request-response | `deploy/Install-PassReset.ps1:77-94` (param block) and `:295-316` (interactive prompt with `-Force` fallback) | exact |
| `deploy/Install-PassReset.ps1` — additive-merge sync replacing `:755-794` drift block | installer / config-mutation | transform | `deploy/Install-PassReset.ps1:760-797` (existing key-path walker `Get-JsonKeyPaths`) | exact |
| `deploy/Install-PassReset.ps1` — fresh-install path at `:644-650` | installer / file-copy | static-asset | itself (lines 640-652) — leave semantics; sync runs only on upgrade branch | exact |
| `deploy/Publish-PassReset.ps1` — copy schema into publish output | build / packaging | static-asset | `deploy/Publish-PassReset.ps1:117` (existing `Copy-Item $templatePath -Destination $publishOut`) | exact |
| `.github/workflows/ci.yml` — Test-Json validation step | CI / build-step | request-response | `.github/workflows/ci.yml:31-43` (existing `run:` steps using `windows-latest` / pwsh) | role-match |

## Pattern Assignments

### `src/PassReset.Web/Models/{ClientSettings,SmtpSettings,SiemSettings,…}Validator.cs` (options-validator, startup-validation)

**Analog:** `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs` (entire file, 35 lines)

**Imports + namespace pattern** (lines 1-3):
```csharp
using Microsoft.Extensions.Options;

namespace PassReset.PasswordProvider;
```
For new validators in `PassReset.Web.Models`, replace the namespace with `namespace PassReset.Web.Models;` (one-class-per-file, file-scoped namespace per project conventions).

**Class shape — `sealed`, `IValidateOptions<T>`** (lines 5-12):
```csharp
/// <summary>
/// Validates <see cref="PasswordChangeOptions"/> at application startup so that
/// mis-configuration is caught immediately with a clear error rather than producing
/// a cryptic LDAP socket error at runtime.
/// </summary>
public sealed class PasswordChangeOptionsValidator : IValidateOptions<PasswordChangeOptions>
{
    public ValidateOptionsResult Validate(string? name, PasswordChangeOptions options)
    {
```

**Validation body — early-return on success, fail with operator-actionable message** (lines 14-33):
```csharp
        if (options.UseAutomaticContext)
            return ValidateOptionsResult.Success;

        if (options.LdapHostnames.Length == 0
            || options.LdapHostnames.All(h => string.IsNullOrWhiteSpace(h)))
        {
            return ValidateOptionsResult.Fail(
                "PasswordChangeOptions.LdapHostnames must contain at least one non-empty hostname " +
                "when UseAutomaticContext is false.");
        }

        if (options.LdapPort <= 0 || options.LdapPort > 65535)
        {
            return ValidateOptionsResult.Fail(
                $"PasswordChangeOptions.LdapPort '{options.LdapPort}' is not a valid port number. " +
                "Use 636 for LDAPS (recommended) or 389 for plain LDAP.");
        }

        return ValidateOptionsResult.Success;
```

**Apply to all new validators.** Conform to D-08 message format: `{field.path}: {reason} (got "{actual}"). Edit {live config path} or run Install-PassReset.ps1 -Reconfigure.` The existing validator pre-dates D-08 — the new ones must include the remediation suffix.

---

### `src/PassReset.Web/Program.cs` (composition-root, startup-validation)

**Analog:** `src/PassReset.Web/Program.cs` itself, lines 33-47 (existing `Configure<T>` block).

**Current pattern — `Configure<T>(GetSection(...))` + manual `AddSingleton<IValidateOptions<T>, …>`** (lines 33-47):
```csharp
builder.Services.Configure<ClientSettings>(
    builder.Configuration.GetSection(nameof(ClientSettings)));
builder.Services.Configure<WebSettings>(
    builder.Configuration.GetSection(nameof(WebSettings)));
builder.Services.Configure<SmtpSettings>(
    builder.Configuration.GetSection(nameof(SmtpSettings)));
builder.Services.Configure<EmailNotificationSettings>(
    builder.Configuration.GetSection(nameof(EmailNotificationSettings)));
builder.Services.Configure<PasswordExpiryNotificationSettings>(
    builder.Configuration.GetSection(nameof(PasswordExpiryNotificationSettings)));
builder.Services.Configure<SiemSettings>(
    builder.Configuration.GetSection(nameof(SiemSettings)));
builder.Services.Configure<PasswordChangeOptions>(
    builder.Configuration.GetSection(nameof(PasswordChangeOptions)));
builder.Services.AddSingleton<IValidateOptions<PasswordChangeOptions>, PasswordChangeOptionsValidator>();
```

**Refactor to `AddOptions<T>().Bind(...).ValidateOnStart()` per D-07** (replace the block above):
```csharp
builder.Services.AddOptions<ClientSettings>()
    .Bind(builder.Configuration.GetSection(nameof(ClientSettings)))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ClientSettings>, ClientSettingsValidator>();

builder.Services.AddOptions<PasswordChangeOptions>()
    .Bind(builder.Configuration.GetSection(nameof(PasswordChangeOptions)))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<PasswordChangeOptions>, PasswordChangeOptionsValidator>();
// … repeat for SmtpSettings, SiemSettings, EmailNotificationSettings,
//    PasswordExpiryNotificationSettings, WebSettings.
```
Keep `AddSingleton<IValidateOptions<T>, …>` per existing convention (line 47) rather than the inline `.Validate(...)` lambda — keeps validation logic out of `Program.cs` and adjacent to the model. `ValidateOnStart()` is the new addition required by D-07 (fail-fast at DI build, ASP.NET Core module returns 502).

**Existing fail-fast pattern at startup — already in use** (lines 84-88):
```csharp
if (webSettings.UseDebugProvider && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "WebSettings.UseDebugProvider is true but Environment is not 'Development'. " +
        "Set UseDebugProvider to false or run in the Development environment.");
```
The startup-validation approach for cross-field rules (D-04) uses the same `throw` style, but inside `IValidateOptions<T>.Validate()` returning `ValidateOptionsResult.Fail(...)` rather than throwing. The bootstrap Serilog logger (lines 16-18) catches the resulting startup exception and writes it before the host dies; for D-07 (Event Log), planner adds a `Serilog.Sinks.EventLog` sink or a `try/catch` around `app.Run()` that writes via `EventLog.WriteEntry("PassReset", …)`.

**Event Log source registration:** the installer owns it (clarification needed — see Claude's Discretion in CONTEXT.md). Pattern: `if (-not [System.Diagnostics.EventLog]::SourceExists('PassReset')) { New-EventLog -LogName Application -Source PassReset }` in `Install-PassReset.ps1` near the existing prerequisites block (`:128-134`).

---

### `src/PassReset.Web/appsettings.Production.template.json` (config-template, static-asset)

**Analog:** itself (currently JSONC with `//` comments at lines 2-6, 171-174).

**Action per D-15:** strip every `//`-prefixed line. The file becomes pure JSON. Move comment content verbatim into `docs/appsettings-Production.md` (existing operator doc). Example header to remove:
```jsonc
// ── Logging ──────────────────────────────────────────────────────────────────
// By default logs go to %SystemDrive%\inetpub\logs\PassReset\passreset-YYYYMMDD.log.
// Override "path" in WriteTo[File].Args to change location. The IIS AppPool identity
// ("IIS AppPool\PassReset" by default) must have Modify rights on the parent folder —
// the installer grants this automatically.
```
Result must validate against the new schema (D-02). CI step in `ci.yml` enforces this via `Test-Json`.

---

### `deploy/Install-PassReset.ps1` — `-ConfigSync` parameter declaration

**Analog:** `deploy/Install-PassReset.ps1:77-94` (existing `[CmdletBinding]` + `param()` block).

**Pattern — add to existing param block** (lines 77-94):
```powershell
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SiteName        = 'PassReset',
    [string] $AppPoolName     = 'PassResetPool',
    [string] $PhysicalPath    = 'C:\inetpub\PassReset',
    [string] $PublishFolder   = '',
    [int]    $HttpsPort       = 443,
    [int]    $HttpPort        = 80,
    [string] $CertThumbprint  = '',
    [string]       $AppPoolIdentity = '',
    [SecureString] $AppPoolPassword = $null,

    [SecureString] $LdapPassword        = $null,
    [SecureString] $SmtpPassword        = $null,
    [SecureString] $RecaptchaPrivateKey  = $null,

    [switch] $Force
)
```

**Add per D-12** (insert before `[switch] $Force`):
```powershell
    [ValidateSet('Merge','Review','None')]
    [string] $ConfigSync = '',   # empty → resolved later: prompt if interactive, 'Merge' if -Force
```

---

### `deploy/Install-PassReset.ps1` — interactive `-ConfigSync` prompt (D-13)

**Analog:** `deploy/Install-PassReset.ps1:295-316` (existing upgrade-confirm prompt with `-Force` fallback).

**Pattern to copy** (lines 295-316):
```powershell
if (-not $Force) {
    $prompt = if ($isDowngrade) {
        '  Continue with DOWNGRADE? [Y/N]'
    } elseif ($isReconfigure) {
        '  Re-configure existing installation? [Y/N]'
    } else {
        '  Continue with upgrade? [Y/N]'
    }
    $confirm = Read-Host $prompt
    if ($confirm -notmatch '^[Yy]') {
        Write-Host "`n  Cancelled." -ForegroundColor Yellow
        exit 0
    }
} else {
    if ($isDowngrade) {
        Write-Warn '-Force specified - proceeding with downgrade despite version regression'
    } elseif ($isReconfigure) {
        Write-Ok '-Force specified - re-configuring without file mirror'
    } else {
        Write-Ok '-Force specified - skipping upgrade confirmation'
    }
}
```

**Apply for `-ConfigSync` resolution** (place AFTER robocopy at `:373` and BEFORE the rewritten drift/sync block at `:755`):
```powershell
if (-not $ConfigSync) {
    if ($Force) {
        $ConfigSync = 'Merge'
        Write-Ok '-Force specified - defaulting to -ConfigSync Merge'
    } elseif ($siteExists) {   # upgrade detected
        $reply = Read-Host '  Config sync: [M]erge additions / [R]eview each / [S]kip? [M]'
        $ConfigSync = switch -Regex ($reply) {
            '^[Rr]' { 'Review' }
            '^[Ss]' { 'None' }
            default { 'Merge' }
        }
    } else {
        $ConfigSync = 'None'   # fresh install — nothing to sync, template is copied fresh
    }
}
```

---

### `deploy/Install-PassReset.ps1` — additive-merge sync replacing drift block at `:755-797`

**Analog:** `deploy/Install-PassReset.ps1:760-797` (existing template-vs-live drift detector with `Get-JsonKeyPaths` recursion).

**Existing structure to rewrite** (lines 760-797):
```powershell
if ($siteExists -and (Test-Path $prodConfig)) {
    $templateFile = Join-Path $PhysicalPath 'appsettings.Production.template.json'
    if (Test-Path $templateFile) {
        try {
            $templateJson = Get-Content $templateFile -Raw | ConvertFrom-Json
            $liveJson     = Get-Content $prodConfig   -Raw | ConvertFrom-Json

            function Get-JsonKeyPaths {
                param($Node, [string]$Prefix = '')
                $paths = @()
                if ($null -ne $Node -and $Node -is [PSCustomObject]) {
                    foreach ($prop in $Node.PSObject.Properties) {
                        $path = if ($Prefix) { "$Prefix.$($prop.Name)" } else { $prop.Name }
                        $paths += $path
                        if ($prop.Value -is [PSCustomObject]) {
                            $paths += Get-JsonKeyPaths -Node $prop.Value -Prefix $path
                        }
                    }
                }
                return $paths
            }

            $templateKeys = Get-JsonKeyPaths -Node $templateJson
            $liveKeys     = Get-JsonKeyPaths -Node $liveJson
            $newKeys      = $templateKeys | Where-Object { $liveKeys -notcontains $_ }

            if ($newKeys) {
                Write-Host ''
                Write-Warn 'Config schema drift detected — new keys in template not present in live config:'
                foreach ($k in $newKeys) { Write-Host "    + $k" -ForegroundColor Yellow }
                Write-Host '    Review appsettings.Production.template.json and add any required keys to' -ForegroundColor Yellow
                Write-Host "    $prodConfig manually." -ForegroundColor Yellow
            }
        } catch {
            Write-Warn "Config schema drift check skipped: $($_.Exception.Message)"
        }
    }
}
```

**Rewrite per D-09/D-10/D-17/D-18:**
1. Read schema (`appsettings.schema.json`) instead of template — schema is authoritative for required keys + defaults.
2. Always run on upgrade (no early-skip when live config parses OK — D-18).
3. Walk schema; collect `(path, default, isObsolete)` triples (`x-passreset-obsolete` from D-11).
4. For each missing path, `Set-` it in the live JSON object using the schema default.
5. Treat arrays as atomic per the spec note in CONTEXT.md "Specific Ideas" — never merge into existing arrays.
6. Branch on `$ConfigSync`:
   - `Merge` → add silently, log each `+ {path}`; report obsolete keys without removing.
   - `Review` → per-key `Read-Host` ("Add 'X' with default Y? [Y/N]" / "Remove obsolete 'X'? [Y/N]" — default N for removal per D-11).
   - `None` → skip block entirely.
7. Write back via `ConvertTo-Json -Depth 32`. Reuse the existing `Get-JsonKeyPaths` recursion shape but switch the source of truth from `$templateJson` to the schema's `properties`/`required` walk.

---

### `deploy/Install-PassReset.ps1` — `Test-Json` pre-flight (D-05)

**Analog:** `deploy/Install-PassReset.ps1:128-134` (prerequisites block; pattern: `Write-Step` → check → `Abort` on failure → `Write-Ok` on success).

**Pattern shape** (lines 128-134):
```powershell
Write-Step 'Checking prerequisites'

if (-not (Get-WindowsFeature W3SVC -ErrorAction SilentlyContinue)?.Installed) {
    Abort 'IIS (W3SVC) is not installed. Install the Web Server (IIS) role first.'
}
Write-Ok 'IIS is installed'
```

**Pre-flight pattern to insert** (after the `appsettings.Production.json` exists check at `:638-640`, before any sync work; runs on upgrade only when live config exists):
```powershell
Write-Step 'Validating appsettings.Production.json against schema'

$schemaFile = Join-Path $PhysicalPath 'appsettings.schema.json'
if (-not (Test-Path $schemaFile)) {
    Write-Warn 'appsettings.schema.json not found in publish output — skipping pre-flight validation.'
} else {
    try {
        $valid = Test-Json -Path $prodConfig -SchemaFile $schemaFile -ErrorAction Stop
        if (-not $valid) {
            Abort "appsettings.Production.json failed schema validation. Edit $prodConfig and retry."
        }
        Write-Ok 'appsettings.Production.json conforms to schema'
    } catch {
        Abort "appsettings.Production.json failed schema validation: $($_.Exception.Message)"
    }
}
```
**PowerShell version gate (Claude's Discretion in CONTEXT.md):** `Test-Json -Schema*` requires PowerShell 6+. Add at top of script (next to `#Requires -RunAsAdministrator` at `:1`):
```powershell
#Requires -Version 7.0
```
or document the bridge approach (`dotnet passreset validate`) — planner records the chosen path in PLAN.md.

---

### `deploy/Publish-PassReset.ps1` — copy schema into publish output

**Analog:** `deploy/Publish-PassReset.ps1:117` (the existing single-line copy of the template).

**Pattern to copy** (line 117):
```powershell
# Copy the production config template into publish output so the installer finds it
Copy-Item $templatePath -Destination $publishOut
```

**Add immediately after** (use the same `$webProject` variable from `:51`):
```powershell
$schemaPath = Join-Path $webProject 'appsettings.schema.json'
Copy-Item $schemaPath -Destination $publishOut
```

The pre-publish key-coverage validator (`:69-93`) — the `Get-JsonKeyPaths` walk that ensures the template covers `appsettings.json` keys — should remain or be replaced by `Test-Json -Path $templatePath -SchemaFile $schemaPath` (D-02 makes the schema authoritative; the appsettings-vs-template walk becomes redundant).

---

### `.github/workflows/ci.yml` — Test-Json validation step

**Analog:** `.github/workflows/ci.yml:31-43` (existing `run:` steps on `windows-latest` — pwsh 7.x available by default).

**Pattern shape** (lines 31-43):
```yaml
      - name: Restore .NET dependencies
        run: dotnet restore src/PassReset.sln

      - name: Build solution
        run: dotnet build src/PassReset.sln --no-restore --configuration Release

      - name: Install npm dependencies
        working-directory: src/PassReset.Web/ClientApp
        run: npm ci

      - name: Build frontend
        working-directory: src/PassReset.Web/ClientApp
        run: npm run build
```

**Add new step (place after `Restore .NET dependencies`, before `Build solution`):**
```yaml
      - name: Validate appsettings.Production.template.json against schema
        shell: pwsh
        run: |
          $valid = Test-Json `
            -Path src/PassReset.Web/appsettings.Production.template.json `
            -SchemaFile src/PassReset.Web/appsettings.schema.json `
            -ErrorAction Stop
          if (-not $valid) { exit 1 }
```
Surface `Test-Json`'s native error stream (line numbers + JSON path) per the "CI validation fail message" note in CONTEXT.md "Specific Ideas".

---

### `src/PassReset.Web/appsettings.schema.json` (NEW — JSON Schema Draft 2020-12)

**Analog:** none in repo (no other JSON Schemas exist). Use the existing template (`appsettings.Production.template.json`) as the structural source — every top-level section becomes a `properties` entry; values become `type` + `default`.

**Pattern shape — operator-facing root** (D-01, D-03, D-04):
```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://passreset.local/schemas/appsettings.schema.json",
  "title": "PassReset appsettings.Production.json",
  "type": "object",
  "additionalProperties": true,
  "required": ["WebSettings", "PasswordChangeOptions", "SmtpSettings", "SiemSettings", "ClientSettings"],
  "properties": {
    "WebSettings": {
      "type": "object",
      "required": ["EnableHttpsRedirect", "UseDebugProvider"],
      "properties": {
        "EnableHttpsRedirect": { "type": "boolean", "default": true },
        "UseDebugProvider":    { "type": "boolean", "default": false }
      }
    },
    "PasswordChangeOptions": {
      "type": "object",
      "required": ["UseAutomaticContext", "PortalLockoutThreshold", "LdapPort"],
      "properties": {
        "UseAutomaticContext":    { "type": "boolean", "default": true },
        "PortalLockoutThreshold": { "type": "integer", "minimum": 1, "default": 3 },
        "PortalLockoutWindow":    { "type": "string", "pattern": "^\\d{2}:\\d{2}:\\d{2}$", "default": "00:30:00" },
        "LdapPort":               { "type": "integer", "minimum": 1, "maximum": 65535, "default": 636 },
        "NotificationEmailStrategy": {
          "type": "string",
          "enum": ["Mail", "UserPrincipalName", "SamAccountNameAtDomain", "Custom"],
          "default": "Mail"
        }
      }
    }
  }
}
```

**Constraint set (D-04):** only `type`, `required`, `enum`, `pattern`, `minimum`/`maximum`, `default`. **Avoid:** `if/then/else`, `oneOf`/`anyOf`, `format` (PowerShell `Test-Json` doesn't enforce `format`). Cross-field rules (e.g., `Recaptcha.Enabled` ⇒ `SiteKey` required) live in C# `IValidateOptions<T>`.

**Obsolete-key marker (D-11):** add `"x-passreset-obsolete": true` and optional `"x-passreset-obsolete-since": "1.3.2"` to deprecated properties — the installer's sync block reads these to drive the per-key prompt.

---

## Shared Patterns

### Operator messaging — `Write-Step` / `Write-Ok` / `Write-Warn` / `Abort`
**Source:** `deploy/Install-PassReset.ps1:101-124`
**Apply to:** every new installer step (Test-Json pre-flight, ConfigSync prompt, additive-merge sync, schema-drift report).
```powershell
function Write-Step  { param([string]$Msg) Write-Host "`n[>>] $Msg" -ForegroundColor Cyan }
function Write-Ok    { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green }
function Write-Warn  { param([string]$Msg) Write-Host "  [!!] $Msg" -ForegroundColor Yellow }
function Abort       { param([string]$Msg) Restore-StoppedForeignSites; Write-Host "`n[ERR] $Msg`n" -ForegroundColor Red; exit 1 }
```

### Interactive prompt with `-Force` fallback (Phase 7 convention)
**Source:** `deploy/Install-PassReset.ps1:295-316`
**Apply to:** `-ConfigSync` resolution prompt; `Review`-mode per-key prompts.
- Interactive: `Read-Host` with `default-on-empty`.
- `-Force` without explicit value: pick the safe default and emit `Write-Ok`/`Write-Warn`.

### Recursive JSON key-path walk
**Source:** `deploy/Install-PassReset.ps1:767-781` (also `deploy/Publish-PassReset.ps1:55-67` — slightly different separator)
**Apply to:** schema walk in additive-merge sync, drift-report enumeration.
- Existing walker uses `.` separator and `PSCustomObject` recursion. The Publish-PassReset walker uses `:` (matches `Microsoft.Extensions.Configuration` env-var convention). Pick one consistently in Phase 8 — `.` for human-facing reports, `:` if the path is also used for env-var override hints.

### Options class registration with validation
**Source:** `src/PassReset.Web/Program.cs:33-47` (existing) + `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs` (existing)
**Apply to:** all six remaining options classes (`ClientSettings`, `WebSettings`, `SmtpSettings`, `SiemSettings`, `EmailNotificationSettings`, `PasswordExpiryNotificationSettings`).
- `AddOptions<T>().Bind(GetSection(...)).ValidateOnStart()` (new per D-07)
- `AddSingleton<IValidateOptions<T>, TValidator>()` (existing convention; one validator per options class, sealed, in same project as the options class)
- Failure surfaces via `OptionsValidationException` at DI build time → ASP.NET Core module returns 502 → Event Log entry under source `PassReset`.

### File-preservation across upgrade
**Source:** `deploy/Install-PassReset.ps1:367-373` (robocopy `/XF`)
**Apply to:** confirm sync writes happen AFTER the robocopy step (current location at `:755-797` is correct — keep that ordering). The schema file (`appsettings.schema.json`) is NOT in `/XF` — it ships with each release and gets overwritten by robocopy, which is what we want.

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `src/PassReset.Web/appsettings.schema.json` | config-schema | static-asset | First JSON Schema in the repo. Use external Draft 2020-12 spec (https://json-schema.org/draft/2020-12) and the existing template as structural reference. Keyword set is constrained per D-04. |

The new validator classes (`ClientSettingsValidator.cs` etc.) have a perfect analog (`PasswordChangeOptionsValidator.cs`) so they are NOT in this list. The Event Log write path (D-07) has no in-repo analog — currently all logging goes through `Serilog`. Planner decides between adding `Serilog.Sinks.EventLog` (NuGet add — vendor-conservative: well-established Serilog sink) or a one-off `EventLog.WriteEntry("PassReset", …)` call in the startup `try/catch`.

---

## Metadata

**Analog search scope:** `src/PassReset.Web/`, `src/PassReset.PasswordProvider/`, `src/PassReset.Common/`, `deploy/`, `.github/workflows/`
**Files scanned:** ~25 (full Models/, Program.cs, all installer/publish PS1, all CI yml)
**Pattern extraction date:** 2026-04-16
