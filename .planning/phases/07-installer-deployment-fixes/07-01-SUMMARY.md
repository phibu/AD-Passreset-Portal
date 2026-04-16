---
phase: 07-installer-deployment-fixes
plan: 01
status: complete
requirements_addressed: [STAB-005]
completed: 2026-04-16
uat_pending: 07-01-HUMAN-UAT.md
---

## What was built

STAB-005 uninstaller parser fix. `deploy/Uninstall-PassReset.ps1` is now UTF-8 with BOM
and contains zero U+2500 BOX DRAWINGS LIGHT HORIZONTAL characters — the two conditions
that together trigger `MissingEndCurlyBrace` ParserError on Windows PowerShell 5.1 per
`07-PATTERNS.md` §STAB-005.

Defensive audit of `deploy/Install-PassReset.ps1` was read-only: the file still contains
U+2500 dividers BUT parses cleanly on both PS 5.1 and PS 7.x, so no change was required
(per plan Task 2 action step 2 fallback).

## Key files

### Modified
- `deploy/Uninstall-PassReset.ps1` — committed as `1b81375`
  - 11 divider lines changed (U+2500 → `-`)
  - File encoding now UTF-8 **with BOM**
  - `git diff` confined to divider-character substitutions — no variable renames, no logic changes, no Write-Host message edits.

### Unchanged (audit only)
- `deploy/Install-PassReset.ps1` — parses cleanly on PS 5.1 + PS 7.x, so the same fix is not required today. Recorded here for future tracking: if Install is edited and re-saved by a tool that strips BOM, the same STAB-005 pattern could regress.

## Acceptance evidence

### Encoding + character checks (PS 5.1)

```
PSVersion:            5.1.26100.8115
Uninstall has BOM:    True       (0xEF 0xBB 0xBF)
Uninstall no U+2500:  True
Install has BOM:      True
Install has U+2500:   True       (intentional — parses fine)
```

### Parse checks

| Shell | Uninstall-PassReset.ps1 | Install-PassReset.ps1 |
|-------|-------------------------|-----------------------|
| Windows PowerShell 5.1 (5.1.26100.8115) | 0 errors | 0 errors |
| PowerShell 7.x (7.6.0) | 0 errors | 0 errors |

Test harness: `[System.Management.Automation.Language.Parser]::ParseFile(<path>, [ref]$null, [ref]$errs)` run via a temporary in-repo script (since deleted).

### Git diff scope

`git show 1b81375 --stat` → `deploy/Uninstall-PassReset.ps1 | 22 +++++++++++-----------`
Only the 11 divider lines touched — one `-` replacement each side (before/after the header text), matching the plan's acceptance criterion.

## Deviations from plan

- None — executor had drifted on the encoding-write technique during the failed subagent run, but the committed result matches the plan's UTF-8-with-BOM + ASCII-divider requirement byte-for-byte.

## Tasks

| Task | Status | Notes |
|------|--------|-------|
| 1 — Replace U+2500, re-save UTF-8+BOM | complete | commit `1b81375` |
| 2 — Parse validation on PS 5.1 + 7.x, defensive Install audit | complete | 0 errors both shells |
| 3 — Operator UAT (human-verify) | **pending** — persisted to `07-01-HUMAN-UAT.md` | user deferred runtime UAT |

## Requirements addressed
- **STAB-005** — ROADMAP Phase 7 Success Criterion #5 (gh#39).

## Self-Check

- [x] Uninstall-PassReset.ps1: first 3 bytes are `0xEF 0xBB 0xBF`.
- [x] Zero U+2500 chars remaining in Uninstall-PassReset.ps1.
- [x] PS 5.1 `Parser::ParseFile` → 0 errors for both deploy scripts.
- [x] PS 7.x `Parser::ParseFile` → 0 errors for both deploy scripts.
- [x] `git diff 1b81375^..1b81375 -- deploy/Uninstall-PassReset.ps1` shows only cosmetic divider changes.
- [ ] Operator UAT on real IIS box (steps 1-4) — persisted to `07-01-HUMAN-UAT.md` for follow-up via `/gsd-verify-work 07`.
