---
phase: 08-config-schema-sync
plan: 05
subsystem: installer
tags: [installer, config-sync, schema, additive-merge, stab-010]
requires: [08-01, 08-04]
provides:
  - "Schema-driven additive-merge config sync (Sync-AppSettingsAgainstSchema) in deploy/Install-PassReset.ps1"
  - "Four reusable JSON-tree helpers (Get-SchemaKeyManifest, Get-LiveValueAtPath, Set-LiveValueAtPath, Remove-LiveValueAtPath) that plan 08-06 layers its drift-check on top of"
affects: [deploy/Install-PassReset.ps1]
tech-stack:
  added: []
  patterns:
    - "D-09/D-10 schema as source of truth — walker traverses schema.properties, NOT the template"
    - "D-13 operator overrides preserved — Set-LiveValueAtPath returns false when leaf already exists"
    - "D-14 arrays atomic — only the whole schema-default array is written when missing; never recursed into when present"
    - "D-11 obsolete keys safe-by-default — Merge reports only; Review prompts with default N for removal"
    - "ConvertTo-Json -Depth 32 write-back, UTF8 no-newline, only when $modified is true"
key-files:
  created: []
  modified:
    - deploy/Install-PassReset.ps1
decisions:
  - "Key-path separator ':' (ASP.NET Core IOptions / env-var convention — operator-facing path 'PasswordChangeOptions:LdapPort' mirrors what env-var or user-secrets workflow uses)"
  - "No-default leaves emit Write-Warn + skip — schema omits 'default:' for secret fields (AD creds, SMTP password, reCAPTCHA private key) so sync never writes placeholder secrets into live config (T-08-20 mitigation)"
  - "Intermediate object auto-creation via Add-Member [PSCustomObject] — handles deep paths like 'SiemSettings:Syslog:Host' where the 'Syslog' sub-object may be missing entirely"
  - "Existing scalar at an intermediate-segment path throws rather than silently overwrites (prevents clobbering an operator who stored '{\"Syslog\":\"disabled\"}' as a string)"
requirements: [STAB-010]
metrics:
  duration: ~20min
  completed: 2026-04-16
---

# Phase 08 Plan 05: Schema-driven additive-merge config sync Summary

STAB-010 — installer now walks `appsettings.schema.json` after every upgrade, adds any missing keys to the operator's live `appsettings.Production.json` using documented schema defaults, and never touches existing values. Arrays are atomic. Obsolete keys (`x-passreset-obsolete: true`) are reported in Merge mode and prompted in Review mode. The naive template-vs-live drift walker (`Get-JsonKeyPaths`) that only *reported* differences is gone.

## What Was Built

### Helper functions (Install-PassReset.ps1 lines 130-330)

| Function | Lines | Purpose |
|----------|-------|---------|
| `Get-SchemaKeyManifest` | 138-167 | Recursive walk of `schema.properties`; emits `[PSCustomObject]` per leaf with Path, Default, HasDefault, IsObsolete, ObsoleteSince, Type |
| `Get-LiveValueAtPath` | 169-188 | Splits `:`-separated path, returns `@{Exists;Value}` without mutating |
| `Set-LiveValueAtPath` | 190-219 | Creates missing intermediate `[PSCustomObject]` nodes; sets leaf; returns `$false` if leaf already exists (D-13 never-modify guard) |
| `Remove-LiveValueAtPath` | 221-239 | Review-mode-only obsolete removal |
| `Sync-AppSettingsAgainstSchema` | 241-329 | Entry point; dispatches on Mode = Merge \| Review \| None; writes back via `ConvertTo-Json -Depth 32` only if modified |

### Dispatch block (Install-PassReset.ps1 lines 807-820)

Replaces the 43-line `Get-JsonKeyPaths` drift walker at the old location (plan 08-04 said lines ~813-855 but post-edit it's 807-820):

```powershell
if ($siteExists -and (Test-Path $prodConfig)) {
    Write-Step 'Syncing appsettings.Production.json against schema'
    $schemaFile = Join-Path $PhysicalPath 'appsettings.schema.json'
    Sync-AppSettingsAgainstSchema `
        -SchemaPath $schemaFile `
        -ConfigPath $prodConfig `
        -Mode $ConfigSync
}
```

## Smoke Test Results

Ran `.tmp-smoke-test.ps1` (dot-source-style extraction of the function block, then three targeted tests against the real schema):

### Test 1 — Existing scalar preserved (D-13)

| Input | After Merge |
|-------|-------------|
| `{"WebSettings":{"EnableHttpsRedirect":false}}` | `EnableHttpsRedirect=false` (operator value kept) + 71 schema defaults added |

**Result: PASS** — `EnableHttpsRedirect=false` after sync proves Set-LiveValueAtPath's "return false if leaf exists" branch works.

### Test 2 — Array atomicity (D-14)

| Input | After Merge |
|-------|-------------|
| `{"PasswordChangeOptions":{"AllowedAdGroups":["CustomGroup"]}}` | `AllowedAdGroups=["CustomGroup"]` unchanged + 72 additions |

**Result: PASS** — operator's single-element array preserved byte-for-byte; sync never merged the schema's default array into it.

### Test 3 — None mode is a no-op

| Input | After None-mode sync |
|-------|----------------------|
| `{"WebSettings":{"EnableHttpsRedirect":true}}` | file byte-identical |

**Result: PASS** — early `return` after `Write-Ok 'Config sync skipped'` confirmed.

### Observed schema behaviour during smoke test

- 72 leaf keys with defaults were auto-added on the minimal configs
- 28 no-default keys (mostly `ClientSettings:ChangePasswordForm:*`, `Alerts:*`, `ValidationRegex:EmailRegex`) correctly emitted `Write-Warn ... operator must set manually` — these are user-facing strings and regex patterns where picking a default would ship English into a German-localized portal (T-08-20 spirit: no wrong defaults).

## Key-Path Separator Decision Recap

`:` (set in plan 08-04, applied here). Operator-facing messages read as `PasswordChangeOptions:LdapPort`, matching:
- ASP.NET Core `IOptions<>` binding convention
- Environment variable naming (`PasswordChangeOptions__LdapPort` → displays with `:` replacing `__`)
- `dotnet user-secrets set "PasswordChangeOptions:LdapPort" "636"` command line

Operator can copy a path straight from the sync log into any of those flows. `.` separator was considered and rejected in 08-04.

## Files Modified

- `deploy/Install-PassReset.ps1` — +206 / -40 LOC (function block + dispatch; old `Get-JsonKeyPaths` walker and accompanying template-vs-live diff block deleted)

## Verification Results

| Check | Result |
|-------|--------|
| `pwsh ... ParseFile` parser check | PARSE OK |
| `grep -c 'function Sync-AppSettingsAgainstSchema'` | 1 ✓ |
| `grep -c 'function Get-SchemaKeyManifest'` | 1 ✓ |
| `grep -c 'function Get-LiveValueAtPath'` | 1 ✓ |
| `grep -c 'function Set-LiveValueAtPath'` | 1 ✓ |
| `grep -c 'function Remove-LiveValueAtPath'` | 1 ✓ |
| `grep -c 'x-passreset-obsolete'` | >= 1 (consumed in manifest + sync) ✓ |
| `grep -c 'ConvertTo-Json -Depth 32'` | >= 1 ✓ |
| `grep 'template schema drift'` | 0 (legacy block gone) ✓ |
| `grep 'Get-JsonKeyPaths'` | 0 in Install-PassReset.ps1 (removed) ✓ |
| Smoke Test 1 (preservation) | PASS |
| Smoke Test 2 (array atomicity) | PASS |
| Smoke Test 3 (None mode no-op) | PASS |

## Deviations from Plan

### Pester tests out of scope

Plan asked for "pwsh tests (Pester) if present; else manual syntax parse-check". Repo has no Pester test harness for `deploy/*.ps1`. Applied the fallback: pwsh parser check + in-session smoke test against the real schema using a minimal dot-source extraction (`.tmp-smoke-test.ps1`, deleted post-run). Documented here per the instructions.

### Dispatch block lines 807-820 (plan said ~755-797 / 841-878)

Plan 08-04's line estimates for the old drift block were 813-855; post-08-04 the file grew to 909 LOC pushing the drift block to 836-878. After this plan's edits the file is now 1075 LOC; the dispatch block sits at 807-820 (moved up because 08-05 replaces 43 lines with 14). Not a deviation — just a line-number recalibration — but documenting because plan 08-06 will consume this position.

### Publish-side `Get-JsonKeyPaths` intentionally left

Plan action step 3 said to leave `Get-JsonKeyPaths` in `Publish-PassReset.ps1` if it exists there as a separate copy (plan 08-07 owns publish-side cleanup). Verified — no removal touched `deploy/Publish-PassReset.ps1`.

## Auth Gates

None.

## Known Stubs

None. `$ConfigSync` resolves upstream (08-04) and is fully consumed here. No placeholder defaults written — schema fields without `default:` correctly emit `Write-Warn ... operator must set manually` and are skipped.

## Lessons / Follow-ups

### Schema defaults audit (confirmed — T-08-20 mitigation)

During smoke testing the "no default → warn + skip" branch fired 28 times on the minimal config. Spot-checked the relevant schema entries:

| Path | Has default? | Safe? |
|------|--------------|-------|
| `PasswordChangeOptions:LdapPassword` | no | ✓ secret — must be env var |
| `SmtpSettings:Password` | no | ✓ secret |
| `ClientSettings:Recaptcha:PrivateKey` | no | ✓ secret |
| `ClientSettings:ChangePasswordForm:*` | no | ✓ localized UI strings |
| `ClientSettings:Alerts:*` | no | ✓ localized UI strings |
| `ClientSettings:ValidationRegex:EmailRegex` | no | ✓ environment-specific |

No secret field carries a `default:` in the schema. Confirms the plan 08-01 acceptance criterion.

### Minor nit for 08-07 cleanup

The `publish`-side `Publish-PassReset.ps1` likely still has its own `Get-JsonKeyPaths` copy used during CI template assembly. That's plan 08-07's job — flagged here for traceability.

### For 08-06 (drift check)

Plan 08-06's drift-CHECK walker should reuse `Get-SchemaKeyManifest` and `Get-LiveValueAtPath` from this plan rather than duplicate the recursion. Those two helpers already expose `(exists, value)` — all the drift-check needs.

## Self-Check: PASSED

- FOUND: `deploy/Install-PassReset.ps1` (modified)
- FOUND commit: `be770b1` (feat(installer): add schema-driven additive-merge config sync (08-05))
- FOUND: all 9 must_haves truths (verified via grep + smoke test above)
- FOUND: key_links patterns
  - `switch.*ConfigSync` equivalent — implemented as `if ($Mode -eq 'None') { return }` + per-mode branches inside Sync-AppSettingsAgainstSchema (semantically equivalent, avoids redundant early-return in the switch)
  - `schema.*properties` at line 146 (`$Schema.properties.PSObject.Properties`)
- Parser check: PARSE OK under pwsh 7
- Smoke test: 3/3 PASS
