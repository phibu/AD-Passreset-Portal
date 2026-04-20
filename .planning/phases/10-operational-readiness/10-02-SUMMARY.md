---
phase: 10-operational-readiness
plan: 02
subsystem: installer
tags: [installer, deploy, health, stab-019]
requirements_addressed: [STAB-019]
status: awaiting-operator-uat
dependency_graph:
  requires:
    - deploy/Install-PassReset.ps1 (existing STAB-001 URL announcement block)
    - src/PassReset.Web/Controllers/HealthController.cs (plan 10-01 nested /api/health contract)
  provides:
    - deploy/Install-PassReset.ps1 (post-deploy verification block + -SkipHealthCheck switch)
    - deploy/HUMAN-UAT-10-02.md (operator checklist)
  affects:
    - deploy/Install-PassReset.ps1
tech_stack:
  added: []
  patterns:
    - Invoke-WebRequest retry loop with explicit attempt counter (idempotent)
    - Hard-fail installer pattern (exit 1 + last response body)
    - Air-gapped opt-out switch (default $false)
key_files:
  created:
    - deploy/HUMAN-UAT-10-02.md
    - .planning/phases/10-operational-readiness/10-02-CHECKPOINT.md
  modified:
    - deploy/Install-PassReset.ps1
decisions:
  - Kept -Force semantics untouched — verification still runs under -Force per D-06/D-07. Only -SkipHealthCheck (new, default off) bypasses.
  - Used ASCII-only 'Health OK --' banner with literal double-hyphen separator per D-08 (no em-dash, no emoji).
  - Reused existing $hostHeader / $selectedHttpPort / $HttpsPort / $CertThumbprint variables in scope at the STAB-001 block — no new config knobs.
  - Pester automation deferred to manual UAT per CONTEXT.md R-05 — installer UAT is the canonical verification path (operator-driven, IIS + AD required).
metrics:
  duration_minutes: ~15
  completed: 2026-04-20
  tasks_completed: 2
  tasks_pending: 1  # Task 3 = operator UAT checkpoint
  tests_added: 0    # per CONTEXT.md R-05 (Pester out of scope)
  files_created: 2
  files_modified: 1
---

# Phase 10 Plan 02: Post-deploy /api/health verification — Summary (interim)

One-liner: Added a `-SkipHealthCheck`-aware post-deploy verification block
to `Install-PassReset.ps1` that hits `/api/health` + `/api/password` with
a 10 x 2 s retry loop, hard-fails with `exit 1` on timeout, and prints an
ASCII-only `Health OK -- AD: ..., SMTP: ..., ExpiryService: ...` banner
on success.

## Status

**Tasks 1 and 2 are complete and committed.** Task 3 is an operator UAT
checkpoint (`type="checkpoint:human-verify" gate="blocking"`) that
requires a Windows Server VM with IIS + AD domain-join. The executor
cannot run this checkpoint from the dev workstation. See
`10-02-CHECKPOINT.md` for the handoff and `deploy/HUMAN-UAT-10-02.md`
for the operator checklist.

This SUMMARY will be updated with final UAT outcome (pass / deferred)
and marked complete when the operator signs off and the orchestrator is
resumed with `"approved"` or `"deferred — no VM"`.

## Tasks executed

### Task 1 — installer post-deploy verification block (commit `d579046`)

Modified `deploy/Install-PassReset.ps1`:

- Added `[switch] $SkipHealthCheck = $false` to the top-level `param()`
  block, adjacent to `[switch] $Force`. Default off — verification runs
  by default.
- Inserted a new STAB-019 block directly after the existing STAB-001
  URL announcement (preserving all prior behaviour). The block:
  - Builds `$baseUrl` from `$hostHeader`/`$selectedHttpPort`/`$HttpsPort`/`$CertThumbprint`.
  - Loops 10 times at 2 s intervals calling `Invoke-WebRequest`
    against `/api/health` and `/api/password` with
    `-UseBasicParsing -TimeoutSec 5 -ErrorAction Stop`.
  - On timeout: `Write-Error "Post-deploy health check failed after 10 attempts. Last /api/health response: ..."` + `exit 1`.
  - On success: `ConvertFrom-Json` the `/api/health` body and
    `Write-Ok "Health OK -- AD: $ad, SMTP: $smtp, ExpiryService: $expir"`.
  - Under `-SkipHealthCheck`:
    `Write-Step "Skipping post-deploy health check (-SkipHealthCheck specified)"`.

### Task 2 — operator UAT checklist (commit `bedc341`)

Created `deploy/HUMAN-UAT-10-02.md` with prerequisites, four scenarios
(A success, B failure with AppPool stop, C `-SkipHealthCheck`,
D `-Force` still verifies), operator sign-off table, Phase 7 deferral
pattern, and explicit note that Pester automation is out of scope per
Phase 10 CONTEXT.md R-05.

## Verification

- `pwsh -NoProfile -Command "[scriptblock]::Create((Get-Content -Raw deploy/Install-PassReset.ps1))"` → parse OK (STAB-005 regression guard).
- `grep "\[switch\] \$SkipHealthCheck" deploy/Install-PassReset.ps1` → 1 match on line 101 (inside `param()`).
- `grep "STAB-019" deploy/Install-PassReset.ps1` → 3 matches (param-block comment + opening + closing block markers).
- `grep "Invoke-WebRequest -Uri \"\$baseUrl/api/health\"" deploy/Install-PassReset.ps1` → match present.
- `grep "Invoke-WebRequest -Uri \"\$baseUrl/api/password\"" deploy/Install-PassReset.ps1` → match present.
- `grep "Start-Sleep -Seconds 2" deploy/Install-PassReset.ps1` → match inside the new block.
- `$maxAttempts  = 10` present.
- `exit 1` present in the new block (plus the pre-existing `Abort` helper — distinct).
- Emoji scan via `[\uD83C-\uD83E][\uDC00-\uDFFF]` regex → zero matches.
- `deploy/HUMAN-UAT-10-02.md` exists and contains the four required scenario headings, the verbatim `Post-deploy health check failed after 10 attempts` string, the `-SkipHealthCheck` reference, the Pester/R-05 out-of-scope note, and the sign-off table.

## Acceptance criteria — Tasks 1 and 2 met

All `acceptance_criteria` entries from both tasks are satisfied. See
plan 10-02 `<acceptance_criteria>` blocks for the full list.

## Deviations from Plan

None. Plan executed as written, with the optional ASCII-only fallback
message on JSON parse failure also using the literal `--` separator
(not an em-dash) for D-08 consistency.

## Threat Flags

None introduced beyond the register in the plan's `<threat_model>`:

- T-10-02-01 (Elevation / fail-open) → mitigated: `exit 1` present on
  the failure path; no retry-forever loop; no warning-only fallback.
- T-10-02-05 (Script parse regression) → mitigated: the static
  `[scriptblock]::Create` parse check passes.

T-10-02-02, T-10-02-03, T-10-02-04 accepted per plan — unchanged.

## Known Stubs

None.

## Pending

- **Task 3 (operator UAT)** — blocking checkpoint. See
  `10-02-CHECKPOINT.md` for handoff instructions and
  `deploy/HUMAN-UAT-10-02.md` for the operator runbook.

## Self-Check: PASSED (Tasks 1 and 2)

- `deploy/Install-PassReset.ps1` — FOUND (modified, parse OK, grep checks pass).
- `deploy/HUMAN-UAT-10-02.md` — FOUND (4 scenarios, sign-off, R-05 note, verbatim error string).
- `.planning/phases/10-operational-readiness/10-02-CHECKPOINT.md` — FOUND.
- Commit `d579046` (Task 1) — confirmed in `git log`.
- Commit `bedc341` (Task 2) — confirmed in `git log`.
