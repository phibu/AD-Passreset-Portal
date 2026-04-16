# Upgrading PassReset

How to upgrade an existing PassReset installation safely, and what to check between releases.

For the full change history, see [CHANGELOG.md](CHANGELOG.md).

---

## General upgrade procedure

1. Download the target release zip from [GitHub Releases](https://github.com/phibu/AD-Passreset-Portal/releases).
2. Extract it on the IIS host.
3. Run the installer against the extracted folder:

   ```powershell
   .\Install-PassReset.ps1 -Force -CertThumbprint "YOUR_CERT_THUMBPRINT"
   ```

   The installer detects the existing installation, shows a version comparison, and creates a dated backup (`PassReset_backup_YYYYMMDD-HHMM\`) before overwriting. `-Force` skips the interactive confirmation for unattended use.

4. The installer preserves:
   - the existing `appsettings.Production.json` (values are not overwritten);
   - IIS app pool environment variables holding secrets (`LdapPassword`, `SmtpPassword`, `RecaptchaPrivateKey`).

5. After upgrade, compare `deploy\appsettings.Production.template.json` in the new release against your live `appsettings.Production.json`. New config keys introduced in the release are **not** added automatically — you must copy them in manually if you want the new behavior.

Full installation reference: [`docs/IIS-Setup.md`](docs/IIS-Setup.md).
Secret handling: [`docs/Secret-Management.md`](docs/Secret-Management.md).

---

## Version-specific notes

### v1.4.0 — Configuration schema and sync

Phase 8 introduces a JSON Schema (`appsettings.schema.json`) that governs every valid key in
`appsettings.Production.json`. The installer now validates your live configuration on every
upgrade and can sync new keys from the schema's defaults without overwriting your customizations.

#### Breaking changes

- **PowerShell 7+ required.** `Install-PassReset.ps1` now uses `Test-Json -SchemaFile`, which is
  unavailable in Windows PowerShell 5.1. Install PowerShell 7
  (`winget install Microsoft.PowerShell`) and invoke the installer via
  `pwsh -File .\Install-PassReset.ps1`. Running under `powershell.exe` fails at the
  `#Requires -Version 7.0` line.

- **`appsettings.Production.template.json` is now pure JSON.** Inline `//` comments have been
  removed so the template parses cleanly with `Test-Json`, `System.Text.Json`, and every other
  strict JSON consumer. All operator-facing notes that previously lived as inline comments are
  preserved verbatim in [`docs/appsettings-Production.md`](docs/appsettings-Production.md#section-notes-formerly-inline-in-template).

#### New parameter: `-ConfigSync <Merge|Review|None>`

Controls how the installer reconciles your live `appsettings.Production.json` with the schema
on upgrade.

| Mode | Behavior |
|------|----------|
| `Merge` (default on upgrade) | Adds missing keys using schema defaults. NEVER modifies existing values. NEVER merges array contents — arrays are atomic. Reports obsolete keys (`x-passreset-obsolete: true`) without removing them. |
| `Review` | Prompts per-key for each missing key (default Yes) and each obsolete key (default No). |
| `None` | Skips sync entirely. The schema-drift check still runs and reports differences, but no file is written. |

When `-ConfigSync` is omitted, the installer resolves a default based on the scenario:

- **Interactive upgrade** — prompts `Config sync: [M]erge additions / [R]eview each / [S]kip? [M]`
- **With `-Force`** — defaults to `Merge` and logs the choice
- **Fresh install** — resolves to `None` silently (template is copied fresh; no live config to sync)

Explicit `-ConfigSync <value>` always wins over the default resolution.

#### New pre-flight validation

Before resolving `-ConfigSync`, the installer runs:

```powershell
Test-Json -Path appsettings.Production.json -SchemaFile appsettings.schema.json
```

Failure halts the install with the offending field path and reason — no sync ever operates on
an invalid file. Edit the live config to fix the reported field, then re-run the installer.

#### Unconditional schema-drift check

After sync (or in `None` mode, after skipping sync), the installer runs an unconditional
drift check that walks `appsettings.schema.json` as the source of truth and reports:

- **Missing required keys** still absent from the live config (with schema default when
  available, or "no default in schema; manual entry required" for secret fields)
- **Obsolete keys** flagged `x-passreset-obsolete: true` still present in the live config
  (with the `x-passreset-obsolete-since` version)
- **Unknown top-level keys** present in the live config but absent from the schema root
  (informational only — `additionalProperties: true` allows them)

The legacy drift walker silently skipped the pass whenever the live config parsed successfully.
The new check always runs, so schema mismatches never go undetected again.

#### Diagnosing 502 after upgrade

The ASP.NET Core host now fails fast on misconfigured options. Every options class validates
at DI build via `ValidateOnStart()` — a failure throws `OptionsValidationException` before
the first request is served, and IIS returns HTTP **502**.

If you see 502 after an upgrade:

1. Open **Event Viewer** → **Windows Logs** → **Application**.
2. Filter by **Source = `PassReset`**, **Event ID = 1001**.
3. Read the failure lines. Format:

   ```
   Section.Field: reason (got "value"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.
   ```

   Example: `PasswordChangeOptions.LdapPort: must be integer between 1 and 65535 (got "0"). …`

4. Edit `C:\inetpub\PassReset\appsettings.Production.json` to correct the reported fields.
5. Recycle the pool:

   ```powershell
   Restart-WebAppPool -Name PassResetPool
   ```

**Secret redaction:** `SmtpSettings.Password`, `ClientSettings.Recaptcha.PrivateKey`, and LDAP
credentials surface as `(got "<redacted>")` — the actual secret is never written to the event.

**If no `PassReset` event appears in Event Viewer**, the source is not registered on the host.
Re-run `Install-PassReset.ps1` from an elevated `pwsh` session to retry the idempotent
registration, then recycle the pool.

### Upgrading from 1.2.2 → 1.2.3

#### AppPool identity is now preserved on upgrade (BUG-003)

`Install-PassReset.ps1` no longer resets the IIS AppPool identity when
`-AppPoolIdentity` is not passed. If you previously configured a custom
service account (for example `CORP\svc-passreset`) via IIS Manager or a
prior install, the upgrade leaves it in place.

- Fresh installs still default to `ApplicationPoolIdentity`.
- Pass `-AppPoolIdentity CORP\<user> -AppPoolPassword <pw>` to override.
- Built-in identities (`ApplicationPoolIdentity`, `NetworkService`,
  `LocalService`, `LocalSystem`) are also preserved on upgrade.

Verify after upgrade:

```powershell
Get-ItemProperty IIS:\AppPools\PassResetPool -Name processModel.userName
```

The value should match what you had configured pre-upgrade.

#### Optional: internal-CA SMTP trust (BUG-001)

If your SMTP relay uses a certificate issued by an internal CA that is not
in `LocalMachine\Root`, you can now add explicit thumbprints to
`SmtpSettings.TrustedCertificateThumbprints` (SHA-1 or SHA-256 hex). See
[`docs/appsettings-Production.md`](docs/appsettings-Production.md). No change required
for deployments using public CAs or already-trusted internal CAs.

#### Clearer error on minimum-password-age rejection (BUG-002)

Users who retry a password change within the domain's `minPwdAge` now see
a dedicated localized message (`errorPasswordTooRecentlyChanged`) instead
of "Unexpected Error." Override the copy via
`ClientSettings.Alerts.errorPasswordTooRecentlyChanged` if desired.

### 1.2.1 — 2026-04-14

No configuration or behavior changes. Pure dependency and security maintenance (Vite 8 via rolldown, CI token hardening, branch/tag rulesets, Dependabot). Safe rolling upgrade.

### 1.2.0 — 2026-04-13

**Breaking changes — review before upgrading.**

- **`PasswordChangeOptions.AllowOnGroupCheckFailure` now defaults to `false`.** Previously, when both `GetGroups()` and `GetAuthorizationGroups()` failed for a user, the portal would fall through and allow the password change. It now returns `ChangeNotPermitted` unless you explicitly set this option to `true`. Only operators who relied on the permissive fallback need to act; most installations should leave the new default in place.

- **reCAPTCHA score threshold is now a config key.** `Recaptcha.ScoreThreshold` replaces the previously hardcoded `0.5`. The default is still `0.5`, so no action is required unless you want to tune it.

**New optional config keys** — pull from `deploy\appsettings.Production.template.json` after upgrade if you want to adopt them:

- **Serilog file logging** — rolling logs at `%SystemDrive%\inetpub\logs\PassReset\passreset-YYYYMMDD.log`. The installer creates this folder and grants `Modify` to the app pool identity on every run, so fresh installs and upgrades both get the correct ACL automatically.
- **`Recaptcha.FailOpenOnUnavailable`** (default `false`) — distinguishes reCAPTCHA network errors from low scores.
- **SMTP retry** (3 attempts, exponential 1s/10s/60s backoff) — automatic, no config change needed.
- **`/api/health` lockout monitoring** — response now includes `lockout.activeEntries`. Scrape-able without opt-in.

A startup configuration validator was also added: error-level issues (debug provider in production, reCAPTCHA enabled with no key) now abort startup. Warning-level issues (email enabled without SMTP, high lockout threshold) are logged. If the app fails to start after upgrade, check the Serilog file or event log for a `ConfigurationValidationException`.

### 1.1.1 — 2026-04-01

**Secrets moved out of `appsettings.Production.json`.** The installer now accepts `-LdapPassword`, `-SmtpPassword`, and `-RecaptchaPrivateKey` as `SecureString` parameters and writes them as IIS app pool environment variables scoped to the pool. JSON config files no longer hold plaintext secrets.

For existing installations:

- Upgrading preserves any previously set environment variables and existing JSON values — nothing breaks on upgrade.
- To migrate an older installation to the new model, re-run the installer with the secret parameters, or set the app pool environment variables manually. See [`docs/Secret-Management.md`](docs/Secret-Management.md) for the PowerShell commands.

The installer also switched to using `deploy\appsettings.Production.template.json` as the single source of truth for the starter production config. `Publish-PassReset.ps1` fails the build if any config key in `appsettings.json` is missing from the template, so shipped releases cannot fall out of sync.

### 1.1.0 and earlier

See [CHANGELOG.md](CHANGELOG.md) for the full history. No special upgrade actions beyond the general procedure above.

---

## Rollback

The installer is idempotent per-version. To roll back:

1. Download the older release zip from [GitHub Releases](https://github.com/phibu/AD-Passreset-Portal/releases).
2. Re-run `Install-PassReset.ps1 -Force -CertThumbprint "..."` against the older extracted folder.
3. The installer will detect the downgrade, back up the current (newer) deployment to a dated folder, and overwrite.

PassReset is stateless — there is no database, no migrations, and no persisted runtime state to reconcile. Portal lockout counters live in process memory and reset on app pool recycle.

If you rely on a custom config-encryption layer on top of `appsettings.Production.json`, keep your own backups of the encrypted file; the installer's dated backups preserve whatever was on disk, but cannot reconstruct keys you manage externally.
