# Phase 13 — Admin UI + Encrypted Config Storage: Design Spec

**Date:** 2026-04-21
**Milestone:** v2.0.0
**Phase:** 13 (Secure Config Storage)
**Requirement ID:** V2-003
**Status:** Design — pending implementation plan

---

## Goal

Add an in-process admin website at `/admin` for editing PassReset's operator-owned configuration. Persist secrets encrypted on disk via ASP.NET Core Data Protection. Serve the admin UI only on a loopback-bound Kestrel listener (`127.0.0.1:<port>`). No auth layer inside the app — access control is the network boundary.

This replaces the "operator hand-edits `appsettings.Production.json` in Notepad" workflow with a web form while preserving all existing escape hatches (hand-editing still works; env vars still override).

---

## Non-Goals

- **No authentication/authorization system.** There is no user-management story. Anyone who can reach `127.0.0.1` on the admin port is treated as a trusted operator.
- **No centralized secret store.** No Azure Key Vault, HashiCorp Vault, or similar integration.
- **No live-reload / hot-swap of config values.** Config changes require an app pool recycle. A Recycle button is provided in the admin UI; operators can still use `Restart-WebAppPool` directly.
- **No full replacement for `appsettings.Production.json`.** Rarely-touched settings (e.g., `AllowedUsernameAttributes`, `PortalLockoutWindow`, `PasswordExpiryNotification` schedule) stay in JSON; admin UI covers the high-traffic subset defined below.
- **No remote admin access in v1.** Future work can add an IP allow-list feature if operator demand materializes.
- **No admin UI-side audit log.** Razor-Pages structured logs go to the existing ASP.NET Core pipeline; the existing SIEM layer captures nothing new.

---

## Architecture

### Decisions locked during brainstorming

| Decision | Choice | Rationale |
|---|---|---|
| Secret storage | Encrypt, not hash | All three outbound-auth secrets (`LdapPassword`, `SmtpPassword`, `RecaptchaPrivateKey`) must be used in plaintext to authenticate to external systems. Hash-only is insufficient. |
| Encryption mechanism | ASP.NET Core Data Protection API | Cross-platform (aligns with Phase 11). DPAPI under the hood on Windows; cert-based key protection on Linux. No custom crypto. |
| Persistence format | Two files: JSON for non-secrets + `secrets.dat` for encrypted secrets | Keeps operator mental model (existing JSON stays human-readable and editable). Matches STAB-008 empty-placeholder convention. |
| UI form factor | Standalone Razor Pages area | Independent of the React SPA — admin UI still works if the React bundle is broken. Built-in data-annotations validation, antiforgery, model binding. |
| Access control | Separate Kestrel listener on `127.0.0.1` | Socket-level enforcement cannot be bypassed by a middleware-ordering bug. No `X-Forwarded-For` footgun. |
| Scope of editable fields | "High-traffic" non-secrets + the four secrets | Covers the settings operators actually touch. Rarely-touched settings stay in JSON for v1. |
| Save semantics | Write-to-disk; require app pool recycle | No `IOptions<T>` → `IOptionsMonitor<T>` migration needed. Recycle is a single button click. |

### Unit boundaries

Five isolated components, each testable independently:

1. **`IConfigProtector`** — thin wrapper over `IDataProtector` for string protect/unprotect.
2. **`SecretStore`** — load/save `secrets.dat` as a single encrypted JSON envelope. Depends on `IConfigProtector`.
3. **`SecretConfigurationProvider`** — `IConfigurationProvider` that reads `secrets.dat` at startup and merges decrypted values into the configuration tree at the canonical keys. Registered between JSON sources and environment variables.
4. **`AppSettingsEditor`** — load/save `appsettings.Production.json` preserving key order and unmanaged keys. Independent of secrets.
5. **Admin Razor Pages area** — one page per fieldset; each page uses `SecretStore` and/or `AppSettingsEditor`.

Plus one piece of startup wiring:

6. **Loopback listener + endpoint mapping** — `Program.cs` changes to add the second Kestrel listener and route `/admin/*` only to that listener.

---

## Components

### `IConfigProtector` (new)

**Location:** `src/PassReset.Web/Services/Configuration/IConfigProtector.cs` + `ConfigProtector.cs`

```csharp
public interface IConfigProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

internal sealed class ConfigProtector : IConfigProtector
{
    private readonly IDataProtector _protector;

    public ConfigProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("PassReset.Configuration.v1");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
```

The purpose string `"PassReset.Configuration.v1"` isolates this protector from any other Data Protection consumer (e.g., antiforgery). Incrementing the version suffix at a later date forces a re-encryption cycle.

### `SecretStore` + `SecretBundle` (new)

**Location:** `src/PassReset.Web/Services/Configuration/SecretStore.cs`

```csharp
public sealed record SecretBundle(
    string? LdapPassword,
    string? ServiceAccountPassword,
    string? SmtpPassword,
    string? RecaptchaPrivateKey);

public interface ISecretStore
{
    SecretBundle Load();
    void Save(SecretBundle bundle);
}

internal sealed class SecretStore : ISecretStore
{
    private readonly IConfigProtector _protector;
    private readonly string _path;
    private readonly ILogger<SecretStore> _log;

    // Load: missing file → empty bundle. Read-all-bytes → Unprotect →
    // JsonSerializer.Deserialize<SecretBundle>.

    // Save: JsonSerializer.Serialize(bundle) → Protect → write to tmp → File.Move
    // (atomic).
}
```

**File format**: `secrets.dat` contains a single string — the output of `IConfigProtector.Protect(JsonSerializer.Serialize(bundle))`. Opaque base64. Not human-readable and not meant to be.

**Atomic write**: `File.WriteAllText(path + ".tmp", encrypted)` then `File.Move(path + ".tmp", path, overwrite: true)`. If the process crashes between the two, the original `secrets.dat` survives intact.

**Missing-file semantics**: `Load()` returns `new SecretBundle(null, null, null, null)` on `FileNotFoundException`. First-install does not fail.

### `SecretConfigurationProvider` + source (new)

**Location:** `src/PassReset.Web/Services/Configuration/SecretConfigurationSource.cs`

```csharp
internal sealed class SecretConfigurationSource : IConfigurationSource
{
    public Func<IServiceProvider, ISecretStore> StoreFactory { get; init; } = default!;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new SecretConfigurationProvider(this);
}

internal sealed class SecretConfigurationProvider : ConfigurationProvider
{
    public override void Load()
    {
        var bundle = _source.StoreFactory(_services).Load();
        if (bundle.LdapPassword is not null)
            Data["PasswordChangeOptions:LdapPassword"] = bundle.LdapPassword;
        if (bundle.ServiceAccountPassword is not null)
            Data["PasswordChangeOptions:ServiceAccountPassword"] = bundle.ServiceAccountPassword;
        if (bundle.SmtpPassword is not null)
            Data["SmtpSettings:Password"] = bundle.SmtpPassword;
        if (bundle.RecaptchaPrivateKey is not null)
            Data["ClientSettings:Recaptcha:PrivateKey"] = bundle.RecaptchaPrivateKey;
    }
}
```

**Registration order in the config builder** (in `Program.cs`):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. **`secrets.dat`** via `SecretConfigurationSource` ← new
4. `AddUserSecrets<Program>()` (Development only)
5. `AddEnvironmentVariables()` ← STAB-017 still wins
6. `AddCommandLine(args)`

Because env vars sit after the secret provider, operators using the STAB-017 env-var workflow see zero behavior change.

### `AppSettingsEditor` + `AppSettingsSnapshot` (new)

**Location:** `src/PassReset.Web/Services/Configuration/AppSettingsEditor.cs`

```csharp
public sealed record PasswordChangeSection(
    bool UseAutomaticContext,
    ProviderMode ProviderMode,
    string[] LdapHostnames,
    int LdapPort,
    bool LdapUseSsl,
    string BaseDn,
    string ServiceAccountDn,
    string DefaultDomain,
    List<string> AllowedAdGroups,
    List<string> RestrictedAdGroups,
    LocalPolicySection LocalPolicy);

public sealed record SmtpSection(
    string Host, int Port, string Username, string FromAddress, bool UseStartTls);

public sealed record RecaptchaPublicSection(bool Enabled, string SiteKey);

public sealed record SiemSyslogSection(
    bool Enabled, string Host, int Port, string Protocol);

public sealed record LocalPolicySection(
    string? BannedWordsPath, string? LocalPwnedPasswordsPath, int MinBannedTermLength);

public sealed record AppSettingsSnapshot(
    PasswordChangeSection PasswordChange,
    SmtpSection Smtp,
    RecaptchaPublicSection Recaptcha,
    SiemSyslogSection Siem);

public interface IAppSettingsEditor
{
    AppSettingsSnapshot Load();
    void Save(AppSettingsSnapshot snapshot);
}
```

**Implementation uses `System.Text.Json.Nodes.JsonObject`** (not `JsonDocument`, not `JsonSerializer.Deserialize<T>`). Reason: `JsonObject` preserves property insertion order on round-trip, which means unmanaged keys (comments-via-$schema, `Logging`, `AllowedHosts`, `WebSettings`, site-local additions) survive an edit-and-save cycle in their original position.

**Mutation strategy**: `Load` reads the file into a `JsonObject`, extracts the managed subtree into an `AppSettingsSnapshot`, and returns both. `Save` re-reads the on-disk file (to avoid clobbering concurrent edits by another admin-UI session or hand-editing), mutates only the owned keys, writes back with `WriteIndented = true`.

**Atomic write** same pattern as `SecretStore`.

### `AdminSettings` (new options binding)

**Location:** `src/PassReset.Web/Configuration/AdminSettings.cs`

```csharp
public sealed class AdminSettings
{
    public bool Enabled { get; set; } = true;
    public int LoopbackPort { get; set; } = 5010;
    public string? KeyStorePath { get; set; }
    public string? DataProtectionCertThumbprint { get; set; }
    public string? AppSettingsFilePath { get; set; }
    public string? SecretsFilePath { get; set; }
}
```

**Validator: `AdminSettingsValidator : IValidateOptions<AdminSettings>`** — fail-fast at startup:
- `LoopbackPort` must be in `1024..65535`
- On Linux: when `Enabled` is true, `DataProtectionCertThumbprint` must be non-empty
- Any of `KeyStorePath` / `AppSettingsFilePath` / `SecretsFilePath`, if set, must be an absolute path

### Admin Razor Pages area

**Location:** `src/PassReset.Web/Areas/Admin/Pages/`

Flat page structure, one directory:

```
Areas/Admin/Pages/
  _ViewStart.cshtml              # sets Layout = "_Layout"
  _ViewImports.cshtml            # namespace + tag helpers
  Shared/_Layout.cshtml          # minimal HTML + Bootstrap 5 CDN link
  Shared/_ValidationScriptsPartial.cshtml  # optional; server-side validation is primary
  Index.cshtml(.cs)              # dashboard — "what's configured, what's empty"
  Ldap.cshtml(.cs)               # LDAP mode, hostnames, BaseDn, service account
  Smtp.cshtml(.cs)               # SMTP host/port/username + password
  Recaptcha.cshtml(.cs)          # Enabled, SiteKey, PrivateKey
  Groups.cshtml(.cs)             # Allowed/Restricted AD groups (textarea, one DN per line)
  LocalPolicy.cshtml(.cs)        # BannedWordsPath, LocalPwnedPasswordsPath, MinBannedTermLength
  Siem.cshtml(.cs)               # Syslog enabled/host/port/protocol
  Recycle.cshtml(.cs)            # single POST to recycle app pool via appcmd.exe
```

**Each PageModel pattern:**

```csharp
public sealed class SmtpModel(IAppSettingsEditor editor, ISecretStore secrets) : PageModel
{
    [BindProperty] public SmtpInput Input { get; set; } = new();

    public void OnGet()
    {
        var snap = editor.Load();
        Input = new SmtpInput { Host = snap.Smtp.Host, Port = snap.Smtp.Port, /* ... */ };
        // Password is NOT populated into the form — placeholder-only.
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid) return Page();

        var snap = editor.Load();
        editor.Save(snap with { Smtp = snap.Smtp with { Host = Input.Host, Port = Input.Port /* ... */ } });

        if (!string.IsNullOrEmpty(Input.NewPassword))
        {
            var bundle = secrets.Load();
            secrets.Save(bundle with { SmtpPassword = Input.NewPassword });
        }

        TempData["Success"] = "SMTP settings saved. Recycle the app pool to apply.";
        return RedirectToPage();
    }

    public sealed class SmtpInput
    {
        [Required, StringLength(255)] public string Host { get; set; } = "";
        [Range(1, 65535)] public int Port { get; set; } = 587;
        [StringLength(255)] public string Username { get; set; } = "";
        [StringLength(255)] public string NewPassword { get; set; } = "";
        [EmailAddress, Required] public string FromAddress { get; set; } = "";
        public bool UseStartTls { get; set; } = true;
    }
}
```

**Secret field rendering:**

```html
<label>SMTP Password</label>
<input type="password" asp-for="Input.NewPassword" placeholder="••••••• (leave blank to keep current)" />
```

Empty value → the existing secret is untouched. Any non-empty value → stored as the new secret. The rendered HTML never contains the current plaintext.

**Anti-forgery:** on by default for Razor Pages POST handlers. Keep it.

**Styling:** Bootstrap 5 via a single `<link>` CDN tag in `_Layout.cshtml`. No npm dependency, no build step. Zero JavaScript required for functionality.

### Recycle page

```csharp
public sealed class RecycleModel(IProcessRunner runner) : PageModel
{
    public string? Output { get; private set; }

    public IActionResult OnPost()
    {
        var result = runner.Run(
            "appcmd.exe",
            ["recycle", "apppool", "/apppool.name:PassReset"]);
        Output = result.StdOut + result.StdErr;
        return Page();
    }
}
```

`IProcessRunner` is a tiny wrapper over `System.Diagnostics.Process` to make the page testable without actually launching `appcmd`. Real implementation calls `Process.Start(ProcessStartInfo { FileName = "...", ArgumentList = args })`.

Confirmation pattern: the page renders a `<form method="post">` with a single button labeled "Recycle App Pool Now." Post-click, the stdout/stderr captured from `appcmd` is displayed.

### Loopback listener + routing

**In `Program.cs`:**

```csharp
builder.Services.Configure<AdminSettings>(
    builder.Configuration.GetSection(nameof(AdminSettings)));
builder.Services.AddSingleton<IValidateOptions<AdminSettings>, AdminSettingsValidator>();

var adminSettings = builder.Configuration
    .GetSection(nameof(AdminSettings)).Get<AdminSettings>() ?? new AdminSettings();

builder.Services.AddSingleton<IConfigProtector, ConfigProtector>();
builder.Services.AddSingleton<ISecretStore>(sp => new SecretStore(
    sp.GetRequiredService<IConfigProtector>(),
    adminSettings.SecretsFilePath ?? Path.Combine(AppContext.BaseDirectory, "secrets.dat"),
    sp.GetRequiredService<ILogger<SecretStore>>()));
builder.Services.AddSingleton<IAppSettingsEditor>(sp => new AppSettingsEditor(
    adminSettings.AppSettingsFilePath ?? /* next-to-appsettings default */));
builder.Services.AddSingleton<IProcessRunner, DefaultProcessRunner>();

// Data Protection
var dp = builder.Services.AddDataProtection()
    .SetApplicationName("PassReset")
    .PersistKeysToFileSystem(new DirectoryInfo(
        adminSettings.KeyStorePath ?? /* default */));
if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();
else if (!string.IsNullOrEmpty(adminSettings.DataProtectionCertThumbprint))
    dp.ProtectKeysWithCertificate(adminSettings.DataProtectionCertThumbprint);

// Secret configuration source (must run before AddEnvironmentVariables)
builder.Configuration.Add(new SecretConfigurationSource {
    StoreFactory = sp => sp.GetRequiredService<ISecretStore>() });

// Razor Pages with Area support
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(opts =>
        opts.Conventions.AddAreaPageRoute("Admin", "/Index", "/admin"));

// Kestrel second listener
if (adminSettings.Enabled)
{
    builder.WebHost.ConfigureKestrel(opts =>
        opts.Listen(System.Net.IPAddress.Loopback, adminSettings.LoopbackPort));
}

// ... existing middleware ...

// Route /admin/* ONLY to the loopback listener
if (adminSettings.Enabled)
{
    app.MapWhen(
        ctx => ctx.Connection.LocalPort == adminSettings.LoopbackPort,
        admin =>
        {
            admin.UseMiddleware<LoopbackOnlyGuardMiddleware>();
            admin.UseRouting();
            admin.UseEndpoints(e => e.MapRazorPages());
        });
}
```

**`LoopbackOnlyGuardMiddleware`** — belt-and-braces. Even though `MapWhen` gates by port, this middleware checks `ctx.Connection.RemoteIpAddress?.IsLoopback() == true`; returns 404 otherwise. Defense against a future refactor accidentally wiring `/admin` onto the public listener.

---

## Configuration

### `appsettings.json` additions

```jsonc
"AdminSettings": {
  "Enabled": true,
  "LoopbackPort": 5010,
  "KeyStorePath": null,
  "DataProtectionCertThumbprint": null,
  "AppSettingsFilePath": null,
  "SecretsFilePath": null
}
```

### `appsettings.Production.template.json`

Same block with `Enabled: true`, `LoopbackPort: 5010`, rest null. Installer already copies the template on first install.

### Validator rules (`AdminSettingsValidator`)

- `LoopbackPort` in `[1024, 65535]`
- On Linux + `Enabled`: `DataProtectionCertThumbprint` must be non-empty
- Paths if set must be absolute
- All three path fields may be null (defaults apply)

Fail-fast at startup, consistent with `PasswordChangeOptionsValidator` pattern from Phase 12.

---

## DI Wiring

All new services registered in `Program.cs`:

- `IConfigProtector` → singleton
- `ISecretStore` → singleton
- `IAppSettingsEditor` → singleton
- `IProcessRunner` → singleton
- `AdminSettings` validator → singleton
- `IDataProtectionProvider` → provided by `AddDataProtection()`
- Razor Pages + Area convention → via `AddRazorPages()`
- Kestrel loopback listener → via `ConfigureKestrel`
- `MapWhen` → wraps `MapRazorPages` for loopback-port routing

No Scrutor, no new external NuGet packages beyond what's already in the Web project (`Microsoft.AspNetCore.DataProtection` is part of the ASP.NET Core shared framework).

---

## Installer changes

**`deploy/Install-PassReset.ps1`:**

1. After the existing config-file copy step, create `C:\inetpub\PassReset\keys\` with NTFS ACL:
   - `IIS AppPool\PassReset:(M)` (modify — Data Protection needs to write)
   - `BUILTIN\Administrators:(F)`
   - Inheritance disabled (`/inheritance:r`)
2. Write the updated template with the new `AdminSettings` block to `appsettings.Production.json` on first install (idempotent: skip if the block already exists, matching STAB-007 convention).
3. Post-install summary prints: `"Admin UI: http://localhost:5010/admin (RDP or console to this server to access)."`

**`deploy/Uninstall-PassReset.ps1`**: unchanged. The keys directory is removed alongside the install directory via the existing cleanup path. Operators with custom `KeyStorePath` should be warned in docs to back up keys before uninstalling.

---

## Testing

### Unit tests (`PassReset.Tests`, net10.0)

- **`ConfigProtectorTests`** — Protect/Unprotect round-trip; ciphertext is not stable across calls; purpose-isolation regression. Uses `EphemeralDataProtectionProvider`.
- **`SecretStoreTests`** — missing-file returns empty bundle; round-trip preserves values; file-on-disk is opaque; atomic write via injected `File.Move` fake; partial bundle round-trips.
- **`AppSettingsEditorTests`** — mutate a known key and reload; unmanaged keys survive round-trip; key order preserved byte-for-byte except for mutated values; atomic write.
- **`SecretConfigurationProviderTests`** — seeded `secrets.dat` surfaces decrypted values via `IConfiguration`; env var overrides decrypted value (STAB-017 regression guard); missing `secrets.dat` contributes no keys and no exception.

### Unit tests (`PassReset.Tests.Windows`, net10.0-windows)

- **`AdminSettingsValidatorTests`** — port range; Linux cert requirement (feature-flagged or skip on Windows); relative path rejection; defaults pass.

### Razor Pages integration tests (`PassReset.Tests.Windows`, via `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`)

- `GET /admin/Smtp` returns 200
- `POST /admin/Smtp` with valid body writes the file + redirects with success `TempData`
- `POST /admin/Smtp` with `Port=0` returns 200 with validation error in body
- Empty `NewPassword` on `POST` leaves the stored secret unchanged
- `POST` without antiforgery token returns 400
- Request to `/admin/*` with `LocalPort != LoopbackPort` (simulated via test server) returns 404

### Explicitly out of scope for Phase 13 tests

- Real `appcmd.exe` invocation — covered by a unit test stubbing `IProcessRunner`.
- Live Linux cert-based key storage — validator test only.
- Load/concurrency testing of the admin UI — single operator, low concurrency assumed.
- DPAPI decryption cross-machine (the whole point is that it doesn't work).

---

## Operator Documentation

### New: `docs/Admin-UI.md`

Covers:
- What the admin UI is and why it exists
- Loopback-only access model (RDP/console to the server, then `http://localhost:5010/admin`)
- First-install walkthrough: empty form on first visit, fill in values, save, recycle
- Editing a secret after first install: "leave blank to keep current" semantics
- Credential rotation: change, save, recycle
- Key storage location + **backup guidance** (lose the keys dir → `secrets.dat` is unrecoverable; document backup procedure)
- Linux deployment: cert thumbprint requirement
- Troubleshooting: admin UI unreachable → check `AdminSettings.Enabled`, the loopback port, local firewall, and that the app pool is running
- "How do I disable the admin UI?" → set `AdminSettings.Enabled: false` in `appsettings.Production.json`, recycle

### Updates

- **`docs/Secret-Management.md`**: add "Option 4: Admin UI (Phase 13)" section. Preserve Options 1-3. Update the precedence table to include `SecretConfigurationProvider` between JSON and env vars.
- **`docs/IIS-Setup.md`**: add a paragraph on the loopback port and where the keys directory lives.
- **`docs/appsettings-Production.md`**: document the new `AdminSettings` block with a table of keys.
- **`README.MD`**: one-line mention under security/features.
- **`CLAUDE.md`**: add `AdminSettings` to "Configuration keys to know" + pointer to `docs/Admin-UI.md`.

### `CHANGELOG.md` — `[Unreleased]` → `[2.0.0-alpha.3]` on ship

```markdown
### Added
- **Admin UI + encrypted config storage** ([V2-003]): in-process admin website at
  `/admin` for editing operator-owned configuration. Bound to a loopback-only
  Kestrel listener (`127.0.0.1:<LoopbackPort>`). Secrets are encrypted on disk
  via ASP.NET Core Data Protection; non-secrets remain in plaintext
  `appsettings.Production.json`. Env-var overrides (STAB-017) still win.
  See `docs/Admin-UI.md`.

### Configuration
- `AdminSettings.Enabled` (default: true)
- `AdminSettings.LoopbackPort` (default: 5010)
- `AdminSettings.KeyStorePath` (default: next-to-app keys/)
- `AdminSettings.DataProtectionCertThumbprint` (Linux only)
- `AdminSettings.AppSettingsFilePath` (default: next to the main appsettings file)
- `AdminSettings.SecretsFilePath` (default: next to the app; `secrets.dat`)

### Security
- New socket-level loopback binding for admin UI: impossible to reach admin
  endpoints from the public listener.
- Data Protection purpose isolation (`PassReset.Configuration.v1`).
- Antiforgery tokens required on all admin POSTs.
```

---

## Backward Compatibility

- Existing deployments with plaintext `appsettings.Production.json` continue to work unchanged. `SecretConfigurationProvider` finds no `secrets.dat` and contributes nothing; JSON remains authoritative.
- `AdminSettings.Enabled = false` disables the entire feature: no Kestrel listener is started, no endpoints are mapped, no admin pages are reachable.
- STAB-017 env-var overrides still take precedence over anything written by the admin UI.
- No database migrations. No breaking API changes.

---

## Security Considerations

- **Loopback-only access** is the primary security control. Socket-level enforcement (via the `Kestrel.Listen(IPAddress.Loopback, ...)` binding) is stronger than middleware-based IP filtering because it cannot be bypassed by routing misconfiguration. The `LoopbackOnlyGuardMiddleware` is defense-in-depth.
- **No auth inside the app**: anyone who can reach `127.0.0.1` on the loopback port is trusted. Server security is the responsibility of the OS/AD, not PassReset.
- **Antiforgery on POSTs**: guards against a malicious local process crafting a form submission. Minimal overhead; non-negotiable even on a loopback listener.
- **Secret field "leave blank to keep current"** pattern: the current plaintext never appears in rendered HTML, not even as a default value. Browser inspect/view-source cannot reveal it.
- **Data Protection purpose string** (`PassReset.Configuration.v1`): prevents cross-use of ciphertext with other Data Protection consumers (antiforgery, session state).
- **Key storage**: `C:\inetpub\PassReset\keys\` with restrictive NTFS ACL. Lose the keys dir → `secrets.dat` is unrecoverable; this is documented as a backup requirement.
- **Log discipline**: neither `SecretStore.Save` nor the Razor Pages log the secret plaintext. Only "saved" or "load failed" events, with no value content.

---

## Rollout

- Ships as `2.0.0-alpha.3` on the v2.0 branch.
- No coordination required with Phase 12 (Local Password DB) or Phase 14 (future Web Admin UI — may overlap conceptually; this phase IS that work).
- First-install experience changes: instead of "open Notepad on the server to edit `appsettings.Production.json`," the new flow is "RDP in, open browser to `localhost:5010/admin`, fill in the form."

---

## Open Questions

None at spec time. Two items deferred to implementation decisions (not blocking):

1. **Bootstrap vs. no-CSS styling:** using Bootstrap 5 via CDN is the current design. If the admin server can't reach a CDN (air-gapped), operators see unstyled HTML — still functional. Self-hosting Bootstrap is a one-line change if it becomes a pain point.
2. **Recycle page implementation detail:** `appcmd.exe` path is hardcoded as `"appcmd.exe"`; if it's not on `PATH` for the app pool identity, the button fails. Documented troubleshooting. Absolute path (`%WINDIR%\System32\inetsrv\appcmd.exe`) is a trivial follow-up fix if needed.
