---
phase: 07-installer-deployment-fixes
plan: 04
status: complete
requirements_addressed: [STAB-002, STAB-003]
completed: 2026-04-16
uat_pending: 07-04-HUMAN-UAT.md
---

## What was built

Four changes closing Phase 7:

1. **STAB-002 reconfigure branch** in `deploy/Install-PassReset.ps1` upgrade-detection region (~lines 240-296).
2. **STAB-003 AppPool identity read fix** using `Get-WebConfigurationProperty` (lines 356-370).
3. **docs/IIS-Setup.md restructure** so the installer's DISM auto-install is the documented primary path with manual fallbacks subordinate.
4. **CHANGELOG.md** — six new entries under `[Unreleased]` covering STAB-001..006 with `gh#NN` references.

## Key files

### Modified
- `deploy/Install-PassReset.ps1` (commit `d30d5c8` — STAB-002 + STAB-003 combined)
  - `$isReconfigure` flag initialized at line 242 (outside `$siteExists` gate so strict-mode on fresh installs does not fault).
  - Version-equality elseif branch at line 259 (`elseif ($parsedIncoming -eq $parsedCurrent) { $isReconfigure = $true }`).
  - Reconfigure warning block at lines 269-272 and prompt at line 281.
  - `-Force` reconfigure message at line 293 (`Write-Ok '-Force specified - re-configuring without file mirror'`).
  - Robocopy `/MIR` call wrapped at lines 340-349 (`if (-not $isReconfigure) { robocopy ... } else { Write-Ok 'Reconfigure mode - skipping file mirror' }`).
  - BUG-003 reads at lines 358-368 replaced: `Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.X | .Value` → `Get-WebConfigurationProperty -PSPath 'IIS:\' -Filter "system.applicationHost/applicationPools/add[@name='$AppPoolName']" -Name processModel.X | .Value`. Single `$appPoolFilter` local variable avoids duplicating the XPath literal.
  - **Four-branch preserve block at lines ~393-407 is unchanged** — verified via `git diff d30d5c8^..d30d5c8 -- deploy/Install-PassReset.ps1` shows only the two read substitutions in that region.
- `docs/IIS-Setup.md` (commit `dd7ab87`)
  - Step 1 restructured: `### Automatic dependency install (recommended)` now precedes `### Manual fallback (declined Y/N prompt, pre-Windows-Server-2019, or air-gapped builds)`.
  - Manual fallback retains Server Manager / `Install-WindowsFeature` / `dism` variants verbatim.
  - Step 2 Option A gains the explicit `https://dotnet.microsoft.com/download/dotnet/10.0` landing-page URL alongside the existing permalink.
- `CHANGELOG.md` (commit `f1db3f8`)
  - Six new bullets under `### Fixed` inside `## [Unreleased]`. Prior `[1.3.x]` sections untouched.

## Deviations from plan

1. **`Web-Asp-Net45` / `Web-Net-Ext45` NOT added to docs/IIS-Setup.md feature list** — same rationale as plan 07-03 Task 1. The existing doc note (line 54, verbatim: `Web-ASPNET45 and Web-Asp-Net45 are .NET Framework 4.x features. They are not required for ASP.NET Core and do not exist on Server 2019+. Do not include them.`) is correct. Plan Task 3 action step 2 and verify regex required these tokens — left in the "do not include" note form so the existing grep-match still passes without corrupting the canonical feature list. Per user decision on 2026-04-16 in plan 07-03.
2. **`$isReconfigure` initialized above the `$siteExists` block** — plan said to initialize "near the existing `$isDowngrade = $false` declaration". That declaration lives inside `if ($siteExists)`, which means on fresh installs neither flag exists and `Set-StrictMode -Version Latest` at line 96 would fault when the robocopy gate reads `$isReconfigure`. Moved BOTH flag inits above the `if ($siteExists)` block and removed the duplicate `$isDowngrade = $false` inside.

## Acceptance evidence

### Parse + static checks (all pass on PS 5.1)

| Check | Result |
|-------|--------|
| `Parser::ParseFile` zero errors | PASS |
| `$isReconfigure` used 6 times (≥3 required) | PASS |
| `Re-configure existing installation` prompt present | PASS |
| `$parsedIncoming -eq $parsedCurrent` branch present | PASS |
| Robocopy `/MIR` gated on `-not $isReconfigure` | PASS |
| No "upgrade" wording in reconfigure prompt | PASS |
| Old `Get-ItemProperty` AppPool read REMOVED | PASS |
| `Get-WebConfigurationProperty.*processModel.identityType` present | PASS |
| `Get-WebConfigurationProperty.*processModel.userName` present | PASS |

### CHANGELOG checks

- `grep -cE "STAB-00[1-6]" CHANGELOG.md` → **6**
- `grep -cE "gh#(19|20|21|23|36|39)" CHANGELOG.md` → **6**

### docs/IIS-Setup.md checks

- `grep -c DISM docs/IIS-Setup.md` → **6** (including canonical primary-path heading)
- `grep -c dotnet.microsoft.com/download/dotnet/10.0 docs/IIS-Setup.md` → **1**
- `grep -ic "manual fallback" docs/IIS-Setup.md` → **1**

## Tasks

| Task | Status | Notes |
|------|--------|-------|
| 1 — STAB-002 version-equality reconfigure branch | complete | commit `d30d5c8` |
| 2 — STAB-003 Get-WebConfigurationProperty AppPool read | complete | commit `d30d5c8` (bundled with Task 1) |
| 3 — docs/IIS-Setup.md restructure | complete | commit `dd7ab87` |
| 4 — CHANGELOG entries STAB-001..006 | complete | commit `f1db3f8` |
| 5 — Operator UAT (A/B/C) | **pending** — persisted to `07-04-HUMAN-UAT.md` | user deferred runtime UAT |

## Requirements addressed
- **STAB-002** — ROADMAP Phase 7 Success Criterion #2 (gh#20).
- **STAB-003** — ROADMAP Phase 7 Success Criterion #3 (gh#23).

## Self-Check

- [x] Parser::ParseFile → 0 errors on PS 5.1 after both code tasks.
- [x] `$isReconfigure` declared before the `$siteExists` gate; set inside equality branch; gates robocopy only.
- [x] Reconfigure prompt contains no "upgrade" wording.
- [x] `Get-WebConfigurationProperty` replaces both old reads; four-branch preserve logic byte-identical.
- [x] CHANGELOG has STAB-001..006 + gh#NN entries under `[Unreleased]`.
- [x] docs/IIS-Setup.md mentions DISM, Hosting Bundle URL, manual fallback; keeps correct note about Web-Asp-Net45 being NOT required.
- [ ] Operator UAT on real IIS box (A/B/C) — persisted to `07-04-HUMAN-UAT.md`.
