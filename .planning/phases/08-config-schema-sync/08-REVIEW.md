---
phase: 08-config-schema-sync
reviewed: 2026-04-17T00:00:00Z
depth: standard
files_reviewed: 20
files_reviewed_list:
  - .github/workflows/ci.yml
  - CHANGELOG.md
  - UPGRADING.md
  - deploy/Install-PassReset.ps1
  - deploy/Publish-PassReset.ps1
  - docs/IIS-Setup.md
  - docs/appsettings-Production.md
  - src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs
  - src/PassReset.Tests/Web/Startup/StartupValidationTests.cs
  - src/PassReset.Web/Models/ClientSettingsValidator.cs
  - src/PassReset.Web/Models/EmailNotificationSettingsValidator.cs
  - src/PassReset.Web/Models/PasswordExpiryNotificationSettingsValidator.cs
  - src/PassReset.Web/Models/SiemSettingsValidator.cs
  - src/PassReset.Web/Models/SmtpSettingsValidator.cs
  - src/PassReset.Web/Models/WebSettingsValidator.cs
  - src/PassReset.Web/PassReset.Web.csproj
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Services/StartupValidationFailureLogger.cs
  - src/PassReset.Web/appsettings.Production.template.json
  - src/PassReset.Web/appsettings.schema.json
findings:
  critical: 0
  warning: 4
  info: 7
  total: 11
status: issues_found
---

# Phase 08: Code Review Report

**Reviewed:** 2026-04-17
**Depth:** standard
**Files Reviewed:** 20
**Status:** issues_found

## Summary

Phase 08 introduces a robust schema-driven configuration story: JSON Schema (keyword-restricted per D-04) as the single source of truth, `IValidateOptions<T>` validators wired via `ValidateOnStart()`, Windows Event Log surfacing of startup validation failures, an installer-side additive merge with a `Test-Json` pre-flight gate, and an unconditional schema-drift check.

Architecture and overall quality are good. Validators are sealed, factor a shared `Fmt` helper, and accumulate failures into a list before returning (pattern consistent across Smtp/Siem/Client/EmailNotification/PasswordExpiry). The installer's "never overwrite operator values" guarantee is enforced by `Set-LiveValueAtPath` returning false on existing keys (D-13), and obsolete-key removal is gated behind explicit `Review` mode (D-11). `Recaptcha.PrivateKey` stays off `ClientSettings` exposed to the client (no changes in scope), and `UseDebugProvider` stays on `WebSettings` — both CLAUDE.md invariants preserved.

No Critical findings. Four Warnings cover (a) weak regression coverage for six of the seven new validators, (b) one early-return in `PasswordChangeOptionsValidator` inconsistent with its peers, (c) one schema default that violates its own validator and will be written by the installer, and (d) a strict-mode-sensitive property access in the PowerShell schema walker. Info items cover minor drift, an unvalidated operator-controlled static-file root, and formatting nits.

## Warnings

### WR-01: Test coverage for new validators is minimal — only `PasswordChangeOptions` is exercised

**File:** `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs`
**Issue:** Phase 08 adds seven new `IValidateOptions<T>` implementations (Client, Web, Smtp, Siem, EmailNotification, PasswordExpiry, PasswordChange). The test file contains a single `[Fact]` covering `PasswordChangeOptions` (empty `LdapHostnames` + out-of-range `LdapPort`). The other six validators — including the two that gate secrets (`ClientSettingsValidator` for `Recaptcha.PrivateKey`, `SmtpSettingsValidator` for the `Username` ↔ `Password` XOR rule) — have no startup-level regression test. Future edits can silently regress the "fail-fast at DI build" contract that Phase 08 is built around.
**Fix:** Add one `[Fact]` per validator with its own `WebApplicationFactory<Program>` subclass (mirroring `InvalidPasswordChangeOptionsFactory`) that sets a single invalid value and asserts `OptionsValidationException` / D-08 message text surfaces. Minimal set:
- `ClientSettings.Recaptcha.Enabled=true` + empty `PrivateKey`
- `SmtpSettings.Username` set + `Password` empty
- `SiemSettings.AlertEmail.Enabled=true` + empty `Recipients`
- `EmailNotificationSettings.Enabled=true` + empty `BodyTemplate`
- `PasswordExpiryNotificationSettings.Enabled=true` + `PassResetUrl=http://...`
```csharp
public sealed class InvalidSmtpFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Development");
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WebSettings:UseDebugProvider"] = "true",
            ["SmtpSettings:Host"]        = "smtp.test",
            ["SmtpSettings:Port"]        = "587",
            ["SmtpSettings:FromAddress"] = "from@test",
            ["SmtpSettings:Username"]    = "user",
            ["SmtpSettings:Password"]    = "",  // XOR violation
        }));
    }
}
```

### WR-02: `PasswordChangeOptionsValidator` short-circuits on first failure, suppressing the second error

**File:** `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs:24-36`
**Issue:** When `UseAutomaticContext=false`, the validator returns `ValidateOptionsResult.Fail(...)` immediately on empty `LdapHostnames` before checking `LdapPort`. A config wrong on both axes surfaces only the hostname error; the operator fixes it, re-runs the installer, and only then sees the port error — two install cycles instead of one. Every peer validator in this phase (Smtp, Siem, Client, EmailNotification, PasswordExpiry) correctly accumulates into a `List<string>` before returning. Inconsistency and a worse operator experience for the one validator most likely to fail on first production deploy.
**Fix:**
```csharp
public ValidateOptionsResult Validate(string? name, PasswordChangeOptions options)
{
    if (options.UseAutomaticContext)
        return ValidateOptionsResult.Success;

    var failures = new List<string>();

    if (options.LdapHostnames.Length == 0
        || options.LdapHostnames.All(h => string.IsNullOrWhiteSpace(h)))
    {
        failures.Add(Fmt("PasswordChangeOptions.LdapHostnames",
            "must contain at least one non-empty hostname when UseAutomaticContext is false", "[]"));
    }

    if (options.LdapPort <= 0 || options.LdapPort > 65535)
    {
        failures.Add(Fmt("PasswordChangeOptions.LdapPort",
            "is not a valid port number (use 636 for LDAPS, 389 for plain LDAP)",
            options.LdapPort.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    return failures.Count == 0
        ? ValidateOptionsResult.Success
        : ValidateOptionsResult.Fail(failures);
}
```

### WR-03: Schema default for `PasswordChangeOptions.LdapHostnames` violates the validator's own rule

**File:** `src/PassReset.Web/appsettings.schema.json:78-82`
**Issue:** Schema declares `LdapHostnames` default as `[ "" ]` (single empty string). The installer's additive merge (`Sync-AppSettingsAgainstSchema`, plan 08-05) writes schema defaults verbatim when a required key is missing. If an operator sets `UseAutomaticContext=false` AND has no prior `LdapHostnames` entry AND runs `-ConfigSync Merge`, the merge writes `[""]` and the subsequent `ValidateOnStart()` then fails with "must contain at least one non-empty hostname" — installer reports success, app fails to start, operator must edit the file by hand. The schema should agree with the validator: default to `[]` so the "missing key + no default" warning path triggers instead of silently writing an invalid value.
**Fix:**
```json
"LdapHostnames": {
  "type": "array",
  "items": { "type": "string" },
  "default": []
}
```
Matches `AllowedAdGroups` pattern. The validator still correctly rejects the empty array when `UseAutomaticContext=false`, but now the merge behavior and the validator behavior are aligned.

### WR-04: PowerShell `Set-StrictMode -Version Latest` + bare property access on schema nodes

**File:** `deploy/Install-PassReset.ps1:160-161`
**Issue:** The installer sets `Set-StrictMode -Version Latest` early (standard practice for this codebase) and `Get-SchemaKeyManifest` reads `$node.'x-passreset-obsolete'` / `$node.'x-passreset-obsolete-since'` directly on every leaf. Under `Latest`, referencing a non-existent property on `[PSCustomObject]` is a `PropertyNotFoundStrict` error. PowerShell 7's current implementation is lenient for PSCustomObject, so most deployments will not hit this, but the behavior is version-sensitive and inconsistent with line 158 where the same helper uses the safer `PSObject.Properties.Name -contains 'default'` idiom. A future PS update or `Strict-Mode 3.0` invocation can regress.
**Fix:** Use the same `-contains` guard for both obsolete markers:
```powershell
IsObsolete    = (($node.PSObject.Properties.Name -contains 'x-passreset-obsolete') -and $node.'x-passreset-obsolete' -eq $true)
ObsoleteSince = if ($node.PSObject.Properties.Name -contains 'x-passreset-obsolete-since') { $node.'x-passreset-obsolete-since' } else { $null }
```

## Info

### IN-01: `PasswordExpiryNotificationSettings.PassResetUrl` — validator enforces `https://`, schema does not

**File:** `src/PassReset.Web/Models/PasswordExpiryNotificationSettingsValidator.cs:47-53` vs. `src/PassReset.Web/appsettings.schema.json:157`
**Issue:** Validator rejects non-`https://` URLs when the feature is enabled. Schema declares `"PassResetUrl": { "type": "string", "default": "" }` with no pattern, so `Test-Json` pre-flight accepts `http://…`. Not a regression (validator wins, operator sees a clean D-08 message), but pre-flight could catch this earlier.
**Fix (optional):** Add `"pattern": "^https://"` to the schema entry. If the keyword-restricted D-04 set excludes `pattern` for this branch, keep as-is.

### IN-02: `EmailNotificationSettings` — validator requires `Subject` + `BodyTemplate` when enabled, schema requires only `Enabled`

**File:** `src/PassReset.Web/Models/EmailNotificationSettingsValidator.cs:23-33` vs. `src/PassReset.Web/appsettings.schema.json:127-138`
**Issue:** Same drift flavor as IN-01, benign — validator stricter than schema, which matches the D-07 pattern (schema = shape, validator = cross-field). Logged for later drift audit.
**Fix:** No action required. JSON Schema `if/then/else` would express this but is outside the D-04 restricted keyword set.

### IN-03: `Program.cs` — operator-supplied `AssetRoot` is served as static files without path validation

**File:** `src/PassReset.Web/Program.cs:273-282`
**Issue:** `clientSettings.Branding?.AssetRoot` is used directly as the root for a `PhysicalFileProvider` mounted at `/brand`. An operator who points this at `C:\Windows\System32` or at the app's own `PhysicalPath` exposes those files at `/brand/*`. `ServeUnknownFileTypes = false` limits the blast radius to known MIME types, but an HTTP 200 for e.g. `/brand/notepad.png` (wrong content behind a known extension) is still undesirable. Operator-controlled config, not user input, so Info not Critical.
**Fix (optional):** In `ClientSettingsValidator`, reject an `AssetRoot` whose `Path.GetFullPath` resolves under `%windir%`, the app's own `PhysicalPath`, or outside a whitelist like `%ProgramData%\PassReset\brand`. Cross-field check, not schema.

### IN-04: `StartupValidationFailureLogger.LogToEventLog` writes to an implicit log name

**File:** `src/PassReset.Web/Services/StartupValidationFailureLogger.cs:34-37`
**Issue:** `EventLog.WriteEntry(source, ...)` resolves the log name via `EventLog.LogNameFromSourceName` at the moment of writing. The installer registers `-LogName Application -Source PassReset` (Install-PassReset.ps1:477), so today this is Application. If a future installer change registers the source under a different log, writes will silently target the new log. Document the coupling or be explicit.
**Fix (optional):**
```csharp
using var log = new EventLog("Application") { Source = EventLogSource };
log.WriteEntry(message, EventLogEntryType.Error, EventId);
```

### IN-05: `Sync-AppSettingsAgainstSchema` rewrites the whole config file via `ConvertTo-Json | Set-Content -NoNewline`

**File:** `deploy/Install-PassReset.ps1:313`
**Issue:** When the merge detects any addition, the entire `appsettings.Production.json` is reserialized. `ConvertTo-Json` in PowerShell 7 preserves `PSCustomObject` insertion order, so existing keys stay put and additions append at object ends — correct for our contract. However, secrets (`LdapPassword`, `SmtpPassword`, `Recaptcha.PrivateKey`) pass through `Get-Content ... | ConvertFrom-Json` in plaintext; a crash between parse and write would leak them to `$Error` / transcripts, and the temp object sits in memory longer than before. Pre-existing risk, not introduced by this phase.
**Fix:** No action required for v2.0. Worth a note in `Secret-Management.md` that Merge mode briefly round-trips secrets in memory. Longer term (v2.1), patch additions only rather than reserializing the whole file.

### IN-06: `ClientSettingsValidator` — verify `ScoreThreshold` literal suffix matches the backing type

**File:** `src/PassReset.Web/Models/ClientSettingsValidator.cs:35`
**Issue:** The comparison uses `0.0f` / `1.0f`. Schema declares `"type": "number"` which `System.Text.Json` binds to `double` unless the property is `float`. If the backing property is `double`, the `f` suffix forces a widening conversion on each compare (harmless functionally, but stylistic mismatch). If it's `float`, current code is correct.
**Fix:** Confirm the type in `ClientSettings.cs`. If `double`, drop the suffix:
```csharp
if (r.ScoreThreshold < 0.0 || r.ScoreThreshold > 1.0)
```

### IN-07: `StartupValidationTests.cs` assertion is intentionally loose — consider tightening once WAF behavior is stable

**File:** `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs:67-72`
**Issue:** The assertion passes if `OptionsValidationException` is in the chain OR if any flattened message contains one of the expected substrings. The OR branch exists per the class comment to tolerate `WebApplicationFactory` re-wrapping. Pragmatic, but a future refactor replacing `OptionsValidationException` with a plain `InvalidOperationException` whose message mentions `LdapHostnames` would still pass. Attempt tightening to `Assert.Contains(chain, e => e is OptionsValidationException)`; revert if WAF re-wrapping breaks CI.
**Fix (optional):** Tighten type check OR narrow the fallback to also require the exception type to be one of `OptionsValidationException` / `HostAbortedException` / `InvalidOperationException` — a generic `Exception` match is broader than intended.

---

_Reviewed: 2026-04-17_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
