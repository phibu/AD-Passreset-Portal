# Phase 07: Installer & Deployment Fixes - Pattern Map

**Mapped:** 2026-04-16
**Files analyzed:** 6 (3 code surfaces + 3 docs)
**Analogs found:** 6 / 6 (all in-file or sibling-file analogs — no greenfield)

> **Scope discipline:** every change in Phase 7 extends an existing pattern in the same file. There are no new files, no new abstractions, and no new dependencies. The planner must reference the line ranges below verbatim — do not invent new conventions.

## File Classification

| Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `deploy/Install-PassReset.ps1` | installer-script (PowerShell) | provisioning / interactive prompt | self (lines 101-104, 144-156, 202-217, 302-351) | exact (in-file) |
| `deploy/Uninstall-PassReset.ps1` | installer-script (PowerShell) | provisioning / removal | `deploy/Install-PassReset.ps1` (helpers + section dividers) | exact (sibling) |
| `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` | provider (AD integration) | request-response / pre-check + AD bind | self (lines 256-283 service-acct fallback; 425-441 hours-remaining; 470-518 catch) | exact (in-file) |
| `src/PassReset.Common/ApiErrorCode.cs` | enum (contract) | n/a — verification only | self (line 73 `PasswordTooRecentlyChanged = 19`) | exact (already exists) |
| `docs/IIS-Setup.md` | operator docs | n/a | existing dependency-install section | role-match |
| `docs/Known-Limitations.md` | operator docs | n/a | existing entries | role-match |
| `CHANGELOG.md` | release notes | n/a | prior `[1.3.x]` entries | role-match |

---

## Pattern Assignments

### `deploy/Install-PassReset.ps1` — STAB-001 (port-80 detection)

**Analog (in-file):** `Install-PassReset.ps1:144-156` (.NET Hosting Bundle detection — same shape: detect → branch → prompt-or-abort with operator-friendly remediation)
**Insertion point:** immediately before `New-Website` call at line ~364 (per CONTEXT D-01).

**Helper-call pattern to reuse** (lines 101-104):
```powershell
function Write-Step  { param([string]$Msg) Write-Host "`n[>>] $Msg" -ForegroundColor Cyan }
function Write-Ok    { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green }
function Write-Warn  { param([string]$Msg) Write-Host "  [!!] $Msg" -ForegroundColor Yellow }
function Abort       { param([string]$Msg) Write-Host "`n[ERR] $Msg`n" -ForegroundColor Red; exit 1 }
```

**Force-mode branching pattern to mirror** (lines 230-243):
```powershell
if (-not $Force) {
    $confirm = Read-Host '  Continue? [Y/N]'
    if ($confirm -notmatch '^[Yy]') { Write-Host "`n  Cancelled." -ForegroundColor Yellow; exit 0 }
} else {
    Write-Ok '-Force specified — proceeding with default'
}
```

**Required behavior** (per D-01/D-02/D-03):
- Detect port-80 binding via `Get-WebBinding -Port 80` (or equivalent), capture site name and protocol.
- Interactive: 3-choice prompt (Stop conflicting site / Use alternate port / Abort) using `Read-Host` and `-notmatch` style branching from the upgrade-detect block.
- `-Force`: scan ports 8080..8090 with `Test-NetConnection -ComputerName localhost -Port $p -InformationLevel Quiet` (or `(Get-NetTCPConnection -LocalPort $p -ErrorAction SilentlyContinue)`); pick first free; abort if all 11 are taken.
- After site creation, print final URL with `Write-Ok "PassReset reachable at http://<host>:<port>/"` so operator can't miss it.

---

### `deploy/Install-PassReset.ps1` — STAB-002 (same-version prompt)

**Analog (in-file):** `Install-PassReset.ps1:202-243` (entire upgrade-detection block).
**Modification:** add an "equal version" branch alongside the existing downgrade/upgrade branches.

**Existing branching at line 217** to extend:
```powershell
if ([version]::TryParse($currentVersion, [ref]$parsedCurrent) -and
    [version]::TryParse($incomingVersion, [ref]$parsedIncoming)) {
    if ($parsedIncoming -lt $parsedCurrent) { $isDowngrade = $true }
    # NEW: elseif ($parsedIncoming -eq $parsedCurrent) { $isReconfigure = $true }
}
```

**Required behavior**:
- When `$parsedIncoming -eq $parsedCurrent`, prompt `"Re-configure existing installation? [Y/N]"` (NOT "upgrade").
- On confirm, **skip the robocopy /MIR mirror at lines 290-296** (no file changes needed) but still re-run app-pool / binding / config logic. Set a `$isReconfigure` flag and gate the robocopy block on `-not $isReconfigure`.
- `-Force` path: log `Write-Ok '-Force specified — re-configuring without file mirror'`.

---

### `deploy/Install-PassReset.ps1` — STAB-003 (AppPool identity read fix)

**Analog (in-file):** `Install-PassReset.ps1:302-356` (entire BUG-003 four-branch block — KEEP the structure, fix only the read).

**The current broken read** (lines 306-315):
```powershell
if ($poolExists) {
    try {
        $existingIdentityType = (Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -ErrorAction Stop).Value
        if ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3) {
            $existingIdentity = (Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.userName -ErrorAction Stop).Value
        }
    } catch {
        Write-Warning "Could not read existing AppPool identity: $($_.Exception.Message). Will fall through to default handling."
    }
}
```

**Diagnosis path** (per CONTEXT — Claude's Discretion on STAB-003):
- The `Get-ItemProperty ... .Value` access returns `$null` on first run after the WebAdministration provider warms up; the second call succeeds. Likely fix candidates (planner picks one):
  1. Replace `Get-ItemProperty | .Value` with `Get-ItemProperty | Select-Object -ExpandProperty Value`.
  2. Use `Get-WebConfigurationProperty -PSPath 'IIS:\' -Filter "system.applicationHost/applicationPools/add[@name='$AppPoolName']" -Name processModel.identityType` (more reliable across PS5.1/7).
  3. Force provider warm-up via `Get-Item "IIS:\AppPools\$AppPoolName" | Out-Null` before the read.
- **Acceptance:** the warning + fallback at line 313 must NEVER fire when the AppPool exists with a valid identity (`SpecificUser`, `ApplicationPoolIdentity`, `NetworkService`, `LocalService`, `LocalSystem`).
- **DO NOT** redesign the four-branch block at lines 330-356. Only fix the read.

---

### `deploy/Install-PassReset.ps1` — STAB-006 (dependency detection)

**Analog (in-file):** `Install-PassReset.ps1:106-156` (entire prerequisites block — extend in-place per D-10 "before publish-folder resolution").

**Existing IIS-feature install pattern** to extend (lines 120-141):
```powershell
$requiredFeatures = @(
    'Web-Server', 'Web-WebServer', 'Web-Static-Content', 'Web-Default-Doc',
    'Web-Http-Errors', 'Web-Http-Logging', 'Web-Filtering', 'Web-Mgmt-Console'
)
$missing = $requiredFeatures | Where-Object {
    (Get-WindowsFeature -Name $_).InstallState -ne 'Installed'
}
if ($missing) {
    Write-Warn "Installing missing IIS features: $($missing -join ', ')"
    Install-WindowsFeature -Name $missing -IncludeManagementTools | Out-Null
    Write-Ok 'IIS features installed'
}
```

**Existing Hosting Bundle detection** (lines 144-156) — KEEP as-is, but change the abort behavior per D-09 to:
- Print download URL + version requirement
- `exit 0` (not `Abort` with exit 1) — clean exit, not error.

**Required behavior** (per D-07/D-08/D-09/D-10):
- Add a single `Y/N` prompt listing ALL missing features in one message (per `<specifics>` — not per-feature).
- On `Y`: run DISM (`dism /online /enable-feature /featurename:<name> /all /norestart`) for each missing feature, gated by `$PSCmdlet.ShouldProcess(...)` (CmdletBinding already supports this — line 77).
- On `N`: print exact remediation commands, exit 0.
- Keep scope to **IIS roles + Hosting Bundle only** — URL Rewrite / ARR are explicitly out (per D-07).

---

### `deploy/Uninstall-PassReset.ps1` — STAB-005 (encoding/parser fix)

**Analog (sibling-file):** `deploy/Install-PassReset.ps1` — same helper functions, same `─── Section ───` divider style (currently using Unicode `─` U+2500 box-drawing chars).

**The likely culprit pattern** (Uninstall-PassReset.ps1 lines 62, 69, 80, 90, etc.):
```powershell
# ─── Helpers ──────────────────────────────────────────────────────────────────
```

**Required behavior** (per CONTEXT — Claude's Discretion on STAB-005):
- **Re-save file as UTF-8 with BOM** (Windows PowerShell 5.1's default-encoding parser is the most likely failure mode without a BOM when Unicode chars are present).
- **Replace all `─` (U+2500) with `---`** (ASCII triple-dash) in section dividers throughout the file.
- **Validate** by parsing on BOTH:
  - Windows PowerShell 5.1 (`powershell.exe -NoProfile -Command "& { [System.Management.Automation.Language.Parser]::ParseFile('Uninstall-PassReset.ps1', [ref]$null, [ref]$null) | Out-Null }"`)
  - PowerShell 7.x (`pwsh.exe ...`)
- **DO NOT** restructure the script (per `<deferred>` discipline — no refactoring opportunity here).
- Apply the same encoding/divider rule to `Install-PassReset.ps1` defensively if it contains the same chars (audit first; if Install passes parser on both shells today, leave it).

---

### `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` — STAB-004 (consecutive-change pre-check)

**Analog 1 (in-file) — service-account-with-fallback context acquisition**: `PasswordChangeProvider.cs:256-283` (`GetUsersInGroup`) and `:209-230` (`GetUserEmail`) both use `AcquirePrincipalContext()` (a private helper that already encapsulates the service-account-vs-bound-user choice via `_options.UseAutomaticContext` / `_options.LdapUsername`). The pre-check MUST call `AcquirePrincipalContext()` — DO NOT bind a new `DirectoryEntry` with the user's typed credentials.

**Pattern to mirror** (lines 256-283):
```csharp
public IEnumerable<(string Username, string Email, DateTime? PasswordLastSet)> GetUsersInGroup(string groupName)
{
    var results = new List<(string, string, DateTime?)>();
    try
    {
        using var ctx   = AcquirePrincipalContext();
        var group = GroupPrincipal.FindByIdentity(ctx, groupName);
        if (group == null) { _logger.LogWarning(...); return results; }
        // ...
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to enumerate members of group {GroupName}", groupName);
    }
    return results;
}
```

**Analog 2 (in-file) — minPwdAge reading**: `PasswordChangeProvider.cs:637-647` (`AcquireDomainMinPasswordAge`) — already exists. The pre-check should CALL this helper, not duplicate the negative-100ns-tick math.

**Analog 3 (in-file) — hours-remaining message shape**: `PasswordChangeProvider.cs:425-441` (the existing `PasswordTooYoung` pre-check):
```csharp
var lastSet = userPrincipal.LastPasswordSet;
if (lastSet == null) return null; // Must-change flag set — exempt from age check

var elapsed = DateTime.UtcNow - lastSet.Value.ToUniversalTime();
if (elapsed >= minAge) return null;

var hoursRemaining = (int)Math.Ceiling((minAge - elapsed).TotalHours);
_logger.LogWarning(
    "User {User} must wait {Hours} more hour(s) before changing password (minPwdAge)",
    userPrincipal.SamAccountName, hoursRemaining);

return new ApiErrorItem(ApiErrorCode.PasswordTooYoung,
    $"Password cannot be changed for another {hoursRemaining} hour(s)");
```

**Analog 4 (in-file) — error mapping convention**: `PasswordChangeProvider.cs:470-518` (the existing `ChangePasswordInternal` catch block) — KEEP as-is per D-05 (defense in depth):
```csharp
catch (System.Runtime.InteropServices.COMException comEx)
{
    const int E_ACCESSDENIED = unchecked((int)0x80070005);
    const int ERROR_DS_CONSTRAINT_VIOLATION = unchecked((int)0x8007202F);
    if (comEx.HResult == E_ACCESSDENIED || comEx.HResult == ERROR_DS_CONSTRAINT_VIOLATION)
    {
        // ... log chain ...
        throw new ApiErrorException(
            "Your password was changed too recently. Please wait before trying again.",
            ApiErrorCode.PasswordTooRecentlyChanged);
    }
    // ... SetPassword fallback gating ...
}
```

**Required behavior** (per D-04/D-05/D-06):
- **Insertion point**: in `PerformPasswordChangeAsync` immediately AFTER authentication succeeds (after the `userPrincipal` is obtained, after the `UserCannotChangePassword` check at line 110, BEFORE the `ChangePasswordInternal` call at line 142).
- Already-existing `PasswordTooYoung` check at lines 425-441 returns the OLD error code (13). The new pre-check uses the NEW code `PasswordTooRecentlyChanged = 19` (line 73 of `ApiErrorCode.cs`) for distinguishability — operators see these as separate events in SIEM.
  - **Planner decision required:** either (a) replace the existing `PasswordTooYoung` return with `PasswordTooRecentlyChanged`, or (b) keep both — which path is per BUG-004 contract? Default to (b) per D-05 (defense in depth — don't remove safety nets) and per `ApiErrorCode.cs` line 70 comment that explicitly distinguishes them.
- Read `pwdLastSet` via `AcquirePrincipalContext()` (D-06 — service account when configured, falls back to bound user). DO NOT require the typing user to have read access on their own `pwdLastSet`.
- Error message format (per `<specifics>`): `"Password was changed Y minutes ago; AD policy requires X minutes between changes (Z minutes remaining)."` — use minutes (not hours) for the new pre-check; the existing `PasswordTooYoung` uses hours.
- Return `new ApiErrorItem(ApiErrorCode.PasswordTooRecentlyChanged, message)` — never throw from the pre-check (provider methods return `ApiErrorItem?`, never throw — the catch block at line 493 throws because it's inside `ChangePasswordInternal` which is the boundary that DOES throw `ApiErrorException`; the controller upstream of `PerformPasswordChangeAsync` converts both to `ApiResult`).

---

### `src/PassReset.Common/ApiErrorCode.cs` — verification

**Analog (in-file):** `ApiErrorCode.cs:73` already defines:
```csharp
PasswordTooRecentlyChanged = 19,
```
with XML doc on lines 68-72 explicitly distinguishing it from `PasswordTooYoung = 13` (line 48). **No code change required** — STAB-004 just consumes the existing constant. Planner action: confirm during implementation, no edit needed.

---

### `docs/IIS-Setup.md` — STAB-006 doc update

**Analog (sibling-file):** existing dependency-install section in this doc (operator-facing remediation walkthroughs).

**Required behavior**:
- Add the new `Y/N` interactive DISM-install path as the **primary** documented flow.
- Keep the existing manual `Install-WindowsFeature` / Server Manager instructions as the **fallback** (operator chose `N`, or running pre-Windows-Server-2019).
- Document that .NET Hosting Bundle is NOT auto-installed (D-09) — link to https://dot.net/download.

---

### `docs/Known-Limitations.md` — STAB-004 doc update

**Analog (sibling-file):** existing entries.
**Required behavior**: REMOVE any "min-pwd-age trips a generic error / `UnauthorizedAccessException`" entry once STAB-004 ships (per CONTEXT canonical_refs).

---

### `CHANGELOG.md` — release notes for all six STAB items

**Analog (sibling-file):** prior `[1.3.x]` entries.
**Required behavior**: under `[Unreleased]` (or new `[1.4.0]` block when cutting), add one line per STAB-001..006 referencing the gh#NN issue from REQUIREMENTS.md lines 19-24. Use existing scope tags (`fix(installer)`, `fix(provider)`, etc. — see commit convention in CLAUDE.md).

---

## Shared Patterns

### PowerShell helper functions (Install + Uninstall)
**Source:** `Install-PassReset.ps1:101-104` (and identical block at `Uninstall-PassReset.ps1:64-67`)
**Apply to:** every new `Write-Host` call in STAB-001/002/003/006 — never invent ad-hoc `Write-Host` formatting.

```powershell
Write-Step '...'   # Cyan, leading newline + [>>] tag — section heading
Write-Ok   '...'   # Green, [OK] tag — success
Write-Warn '...'   # Yellow, [!!] tag — non-fatal warning
Abort      '...'   # Red, [ERR] tag, exit 1 — fatal
```

### Force-mode binary prompt branching
**Source:** `Install-PassReset.ps1:230-243` (upgrade confirmation)
**Apply to:** STAB-001 port-80 prompt, STAB-002 reconfigure prompt, STAB-006 DISM consent prompt.
- Interactive: `Read-Host` + `-notmatch '^[Yy]'` → `exit 0` with friendly cancel message.
- `-Force`: bypass prompt, log `Write-Ok '-Force specified — ...'`.

### `[CmdletBinding(SupportsShouldProcess)]` for new destructive ops
**Source:** `Install-PassReset.ps1:77` already declares this.
**Apply to:** the new DISM `Install-WindowsFeature` calls in STAB-006 — wrap with `if ($PSCmdlet.ShouldProcess("IIS feature $f", "Enable via DISM")) { ... }` so `-WhatIf` works for free.

### Service-account-with-fallback for AD reads
**Source:** `PasswordChangeProvider.cs:256-283` (`GetUsersInGroup`), `:209-230` (`GetUserEmail`), via the private `AcquirePrincipalContext()` helper.
**Apply to:** STAB-004 pre-check — D-06 mandate.

### `ApiErrorItem?`-returns-never-throw provider convention
**Source:** every public method in `PasswordChangeProvider.cs` (lines 209, 256, 286, 299, etc.).
**Apply to:** STAB-004 pre-check return — return `new ApiErrorItem(...)` directly; do NOT throw `ApiErrorException`. Throwing is the catch-block-only convention (line 493) for the AD-bind boundary, not the pre-check.

### `─── Section ───` style with ASCII fallback
**Source (current Unicode form):** `Install-PassReset.ps1:99, 106, 158, 177, ...` and same in Uninstall.
**Source (target ASCII form for STAB-005):** replace `─` (U+2500) with `---`.
**Apply to:** STAB-005 fix in `Uninstall-PassReset.ps1`; audit `Install-PassReset.ps1` defensively.

---

## No Analog Found

**None.** Every modified file extends an existing in-file or sibling-file pattern. This phase is bug-fixing within established conventions — there is no greenfield surface. If the planner identifies a "no analog found" situation during planning, that's a signal to re-read CONTEXT.md and confirm the change is in scope (it may have drifted into a deferred refactor — see `<deferred>` block in CONTEXT.md).

---

## Metadata

**Analog search scope:** `deploy/`, `src/PassReset.PasswordProvider/`, `src/PassReset.Common/`, `docs/`
**Files scanned:** 11 (3 deploy scripts, 5 provider .cs files, 3 common .cs files)
**Pattern extraction date:** 2026-04-16
**Source CONTEXT:** `.planning/phases/07-installer-deployment-fixes/07-CONTEXT.md`
**Source REQUIREMENTS:** `.planning/REQUIREMENTS.md` §v1.4.0 lines 19-24
**Source ROADMAP:** `.planning/ROADMAP.md` §"Phase 7: Installer & Deployment Fixes"
