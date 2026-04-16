---
phase: 07-installer-deployment-fixes
verified: 2026-04-16T00:00:00Z
status: human_needed
score: 6/6 must-haves verified (code) + 5 operator UAT items explicitly deferred by user
overrides_applied: 0
re_verification:
  previous_status: none
  previous_score: n/a
  gaps_closed: []
  gaps_remaining: []
  regressions: []
human_verification:
  - test: "STAB-005 runtime UAT — Uninstall-PassReset.ps1 on real IIS box, PS 5.1 + PS 7.x, with and without -KeepFiles"
    expected: "Script completes without ParserError; IIS site + AppPool removed; -KeepFiles preserves publish folder"
    why_human: "Requires live IIS installation with PassReset present; runtime behavior not parser-provable. Persisted to 07-01-HUMAN-UAT.md — user explicitly deferred on 2026-04-16."
  - test: "STAB-001 runtime UAT Scenario A — Port 80 in use, interactive install"
    expected: "Prompt offers [1] Stop / [2] Alternate port / [3] Abort; choosing [2] completes install with final 'PassReset reachable at http://...:8080/' line"
    why_human: "Requires clean Windows Server test box with Default Web Site bound. Persisted to 07-03-HUMAN-UAT.md — user explicitly deferred."
  - test: "STAB-001 runtime UAT Scenario B — Port 80 in use, -Force install"
    expected: "No prompt; '-Force specified — port 80 in use, defaulting to alternate port 8080' printed; install completes on alternate port"
    why_human: "Requires live IIS with Default Web Site running. Persisted to 07-03-HUMAN-UAT.md — deferred."
  - test: "STAB-006 runtime UAT Scenario C — Missing Web-Server feature with single Y/N DISM install"
    expected: "Single prompt 'Install missing IIS features now via DISM? [Y/N]' lists all missing items; Y → DISM installs; N → prints explicit dism commands and exits 0 cleanly"
    why_human: "Requires simulating missing IIS roles on a test box and invoking DISM. Persisted to 07-03-HUMAN-UAT.md — deferred."
  - test: "STAB-002 + STAB-003 runtime UAT — same-version reconfigure and AppPool identity preservation"
    expected: "Scenario A: 'Re-configure existing installation? [Y/N]' prompt (no 'upgrade' wording), file mirror skipped on Y; Scenario B: -Force logs 're-configuring without file mirror'; Scenario C: upgrade with SpecificUser AppPool identity completes with NO 'Could not read existing AppPool identity' warning and identity preserved post-install"
    why_human: "Requires live IIS box with pre-installed PassReset and configured AppPool SpecificUser. Persisted to 07-04-HUMAN-UAT.md — deferred."
---

# Phase 07: Installer & Deployment Fixes (v1.4.0 Stabilization) Verification Report

**Phase Goal:** Deliver six v1.4.0 stabilization items (STAB-001..006) that harden the PowerShell installer and the AD password provider. STAB-001/002/003/006 target Install-PassReset.ps1 upgrade & dependency-handling; STAB-004 adds the consecutive-change pre-check in the password provider; STAB-005 fixes the uninstaller parser error.

**Verified:** 2026-04-16
**Status:** human_needed (code-level verification complete; 5 runtime UAT items explicitly deferred by user)
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths

| # | Truth (ROADMAP SC) | Status | Evidence |
|---|---|---|---|
| 1 | STAB-001 (gh#19): Fresh install succeeds when IIS Default Web Site holds port 80 | ✓ VERIFIED (code) / ? human_needed (runtime) | `Install-PassReset.ps1:431-491` — `Get-WebBinding -Port 80` detection, 3-choice prompt, `8080..8090` alt-port scan, `-Force` never stops foreign site (lines 476-490). Reachable URL `Write-Ok "PassReset reachable at …"` at :550. Operator UAT deferred. |
| 2 | STAB-002 (gh#20): Same-version re-run prompts 're-configure', not 'upgrade' | ✓ VERIFIED (code) / ? human_needed (runtime) | `$isReconfigure` flag declared at :243 (outside `$siteExists` gate for strict-mode safety), set at :259 via `$parsedIncoming -eq $parsedCurrent`, prompt at :279 `'Re-configure existing installation? [Y/N]'`. Robocopy /MIR gated on `-not $isReconfigure` at :346. Operator UAT deferred. |
| 3 | STAB-003 (gh#23): Upgrade preserves AppPool identity without spurious warning | ✓ VERIFIED (code) / ? human_needed (runtime) | Old `Get-ItemProperty "IIS:\AppPools\$AppPoolName" .Value` removed. Replaced at :372 & :375 with `Get-WebConfigurationProperty -PSPath 'IIS:\' -Filter "…applicationPools/add[@name='$AppPoolName']"` for both identityType and userName. Four-branch preserve logic untouched (per SUMMARY evidence). Operator UAT deferred. |
| 4 | STAB-004 (gh#36): Consecutive password changes return clear UI error, no UnauthorizedAccessException | ✓ VERIFIED | `PasswordChangeProvider.cs:137` call site (after `UserCannotChangePassword`, before `ChangePasswordInternal`). `PreCheckMinPwdAge` at :661-689 uses `AcquirePrincipalContext()` + `AcquireDomainMinPasswordAge()`. Pure `EvaluateMinPwdAge` at :698 returns `ApiErrorItem(PasswordTooRecentlyChanged, …)` at :713. Existing COMException catch at :485-500 with `E_ACCESSDENIED` preserved as defense-in-depth. 6 xUnit tests in `PreCheckMinPwdAgeTests.cs` (14 grep matches on STAB-004 symbols). Full test suite 87/87 passed. |
| 5 | STAB-005 (gh#39): Uninstall-PassReset.ps1 parses on PS 5.1 and PS 7.x | ✓ VERIFIED (code) / ? human_needed (runtime) | File re-saved UTF-8 with BOM (0xEF 0xBB 0xBF). Zero U+2500 characters remaining (grep returned no matches for `─`). Dividers replaced with ASCII `-`, e.g. `# --- Helpers ---` at :62. `Parser::ParseFile` returned 0 errors on both PS 5.1 (5.1.26100.8115) and PS 7.x (7.6.0) per 07-01-SUMMARY.md. `-KeepFiles` logic at :96/98/99/153/163/195/196 intact. Operator runtime UAT deferred. |
| 6 | STAB-006 (gh#21): Installer detects missing IIS roles / Hosting Bundle and offers interactive install | ✓ VERIFIED (code) / ? human_needed (runtime) | `Install-PassReset.ps1:139` — single Y/N consent `'Install missing IIS features now via DISM?'`. :153 ShouldProcess-gated DISM invocation (`$PSCmdlet.ShouldProcess("IIS feature $f", 'Enable via DISM')`). Hosting Bundle missing branch at :176 & :188 prints `https://dotnet.microsoft.com/download/dotnet/10.0` + `exit 0` (no Abort). Web-Asp-Net45 / Web-Net-Ext45 intentionally excluded per documented note at :117 (Server 2019+ doesn't support them; user-confirmed deviation). Operator UAT deferred. |

**Score:** 6/6 code-level truths verified. 5 of 6 ROADMAP SCs have operator runtime UAT still pending (user-deferred).

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `deploy/Uninstall-PassReset.ps1` | UTF-8 with BOM, zero U+2500, `-KeepFiles` intact | ✓ VERIFIED | BOM confirmed per 07-01-SUMMARY; `─` grep returned zero matches; `# --- Helpers ---` at :62; KeepFiles 7 occurrences |
| `deploy/Install-PassReset.ps1` | Port-80 block + DISM prompt + Hosting Bundle exit-0 + reconfigure branch + Get-WebConfigurationProperty | ✓ VERIFIED | All six search patterns match at expected lines; line ordering correct (port-80 block :431 < New-Website :497; dependency block :139 ABOVE `[version]::TryParse` at :255-259) |
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` | PreCheckMinPwdAge method + call site + preserved COMException catch | ✓ VERIFIED | Call site :137, method :661, static evaluator :698, COMException catch :485-500 intact with E_ACCESSDENIED |
| `src/PassReset.Tests/PasswordProvider/PreCheckMinPwdAgeTests.cs` | 6 xUnit tests on evaluator | ✓ VERIFIED | File exists; 14 grep matches on STAB-004 symbols; 6/6 tests passed per 07-02-SUMMARY |
| `CHANGELOG.md` | 6 entries STAB-001..006 + gh#NN refs | ✓ VERIFIED | 6 STAB-NNN matches, 6 gh#NN matches (count=6 each) |
| `docs/IIS-Setup.md` | DISM auto-install primary + Manual fallback + Hosting Bundle URL | ✓ VERIFIED | 9 combined matches for DISM/fallback/URL patterns |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `PerformPasswordChangeAsync` | `PreCheckMinPwdAge` | Direct call at :137 after auth/UserCannotChangePassword, before ChangePasswordInternal | ✓ WIRED | Confirmed via grep line numbers |
| `PreCheckMinPwdAge` | `AcquireDomainMinPasswordAge` | Direct call at :665 | ✓ WIRED | No duplicated tick math; reuses helper |
| `PreCheckMinPwdAge` | `AcquirePrincipalContext` | Service-account-with-fallback at :668 | ✓ WIRED | Matches D-06 pattern |
| Install-PassReset.ps1 prereqs block | `dism /online /enable-feature` | ShouldProcess-gated at :153 | ✓ WIRED | Single Y/N prompt at :139 governs all features |
| Install-PassReset.ps1 port-80 block | `New-Website` | `$selectedHttpPort` consumed at :497 | ✓ WIRED | Detection block at :431 precedes site creation; 13 uses of `$selectedHttpPort` |
| Install-PassReset.ps1 upgrade block | `$isReconfigure` gate | Robocopy wrapped at :346 | ✓ WIRED | Downstream app-pool/config logic NOT gated (correct per plan) |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| STAB-001 | 07-03-PLAN | Port-80 conflict detection | ✓ SATISFIED (code) / human_needed (runtime) | Code verified; runtime UAT deferred to 07-03-HUMAN-UAT.md |
| STAB-002 | 07-04-PLAN | Same-version reconfigure prompt | ✓ SATISFIED (code) / human_needed (runtime) | Code verified; runtime UAT deferred to 07-04-HUMAN-UAT.md |
| STAB-003 | 07-04-PLAN | AppPool identity via Get-WebConfigurationProperty | ✓ SATISFIED (code) / human_needed (runtime) | Old read removed, new read present; runtime UAT deferred |
| STAB-004 | 07-02-PLAN | PreCheckMinPwdAge + PasswordTooRecentlyChanged mapping | ✓ SATISFIED | Code + 6 passing tests + 87/87 full suite |
| STAB-005 | 07-01-PLAN | Uninstaller parser fix | ✓ SATISFIED (code) / human_needed (runtime) | BOM/U+2500 fixed, parser clean on 5.1 + 7.x; runtime UAT deferred |
| STAB-006 | 07-03-PLAN | DISM detection + Hosting Bundle exit-0 | ✓ SATISFIED (code) / human_needed (runtime) | All static patterns match; runtime UAT deferred |

No orphaned requirements — all six declared STAB IDs appear across plans 01-04 and all are accounted for.

### Documented Deviations (user-confirmed)

1. **Web-Asp-Net45 / Web-Net-Ext45 NOT added to $requiredFeatures** — plan 07-03 Task 1 step 2 required these, but the existing code note at `Install-PassReset.ps1:117-118` documents that these are .NET Framework 4.x features that do not exist on Server 2019+. User confirmed this deviation on 2026-04-16 (recorded in 07-03-SUMMARY.md and 07-04-SUMMARY.md). Does NOT fail STAB-006: the roadmap SC only requires "detects missing IIS roles … and offers interactive install", which the implementation does for the correct feature set.
2. **`$isReconfigure` initialized above `$siteExists` gate** — required for strict-mode compatibility (Set-StrictMode on fresh installs would fault on undefined variable). Documented in 07-04-SUMMARY.md.
3. **Port-80 detection guarded with `-not $siteExists -and $selectedHttpPort -eq 80`** — avoids false-positive against PassReset's own existing binding on upgrade path. Documented in 07-03-SUMMARY.md.

### Behavioral Spot-Checks

Full xUnit suite already exercised: **87/87 tests pass** (81 pre-existing + 6 new STAB-004 tests) per 07-02-SUMMARY.md. Parser spot-checks on both deploy PS1 scripts returned 0 errors on PS 5.1 (5.1.26100.8115) and PS 7.x (7.6.0).

### Anti-Patterns Found

Per 07-REVIEW.md: **0 critical, 3 warnings, 4 info findings** — all advisory and non-blocking. No stub/placeholder/TODO patterns observed in the phase's modified files that would compromise goal achievement.

### Human Verification Required

See YAML frontmatter `human_verification:` section. Five operator UAT scenarios (STAB-001/002/003/005/006) require a live Windows Server + IIS test environment and were explicitly deferred by the user on 2026-04-16. Persisted to:
- `07-01-HUMAN-UAT.md` (STAB-005)
- `07-03-HUMAN-UAT.md` (STAB-001, STAB-006)
- `07-04-HUMAN-UAT.md` (STAB-002, STAB-003)

### Gaps Summary

No code-level gaps. All six STAB items have concrete implementation evidence, correct wiring, and where automatable, passing tests. The only outstanding work is the five deferred operator runtime UAT scenarios, which the user has consciously chosen to complete later via `/gsd-verify-work 07` re-entry.

---

_Verified: 2026-04-16_
_Verifier: Claude (gsd-verifier)_
