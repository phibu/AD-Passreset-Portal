---
phase: 10-operational-readiness
plan: 02
task: 3
type: checkpoint
gate: blocking
status: awaiting-operator-uat
created: 2026-04-20
requirement: STAB-019
---

# Plan 10-02 — Task 3 Checkpoint: Operator UAT (STAB-019)

## What was built

### Task 1 — installer post-deploy verification (commit `d579046`)

- Added `[switch] $SkipHealthCheck = $false` to the `param()` block of
  `deploy/Install-PassReset.ps1` (line 101). Defaults off — verification
  runs by default including under `-Force` (D-06/D-07).
- Inserted a new STAB-019 post-deploy verification block directly after
  the existing STAB-001 URL announcement block (around line 939). The
  block:
  - Computes `$baseUrl` from existing `$hostHeader`/`$selectedHttpPort`/`$HttpsPort`/`$CertThumbprint` — no new config knobs.
  - Calls `GET /api/health` and `GET /api/password` via
    `Invoke-WebRequest -UseBasicParsing -TimeoutSec 5`.
  - Retries 10 times at 2-second intervals (~20 s worst case).
  - On final failure: `Write-Error` with last response body, then
    `exit 1` (hard-fail, D-07).
  - On success: parses the `/api/health` JSON and prints
    `Health OK -- AD: <s>, SMTP: <s>, ExpiryService: <s>` via `Write-Ok`
    (ASCII only, no emoji, D-08).
  - When `-SkipHealthCheck` is set, prints
    `Skipping post-deploy health check (-SkipHealthCheck specified)`
    and skips all HTTP calls (D-10).
- PowerShell static parse succeeds
  (`[scriptblock]::Create((Get-Content -Raw deploy/Install-PassReset.ps1))`),
  guarding against the STAB-005 `MissingEndCurlyBrace` regression class.

### Task 2 — HUMAN-UAT checklist (commit `bedc341`)

- Created `deploy/HUMAN-UAT-10-02.md` with four scenarios
  (A success, B failure, C `-SkipHealthCheck`, D `-Force`), prerequisite
  list, operator sign-off table, deferral note pattern from Phase 7, and
  explicit call-out that Pester automation is out of scope per Phase 10
  CONTEXT.md R-05.

## What the operator needs to do

This is a `checkpoint:human-verify` with `gate="blocking"` because the
installer can only be meaningfully exercised on a real Windows Server
VM with IIS + AD domain-join. The automation cannot run this from the
dev workstation (no IIS, no domain controller, no AppPool to stop).

**Instructions:** follow `deploy/HUMAN-UAT-10-02.md` end-to-end on a
clean Windows Server 2019+ VM. All four scenarios must be recorded.
If no VM is available, the operator may sign off as
`DEFERRED — physical host unavailable`, matching the Phase 7 UAT
deferral pattern documented in `.planning/STATE.md`.

## Completion criteria for unblocking Task 3

Any one of the following is acceptable to close the checkpoint:

- **Full pass:** Scenarios A, B, C, D all pass. Record outcomes in the
  sign-off table of `deploy/HUMAN-UAT-10-02.md`, commit the signed
  document, and resume with `"approved"`.
- **Formal deferral:** No test VM is available. Record
  `DEFERRED — physical host unavailable` in each scenario outcome row,
  commit the signed document, and resume with
  `"deferred — no VM"`. Same pattern as Phase 7.
- **Failure:** Any scenario fails. Record the failure mode, file a new
  plan or patch, and do NOT mark plan 10-02 complete.

## Files changed in plan 10-02

| Path                                                        | Change   | Commit     |
| ----------------------------------------------------------- | -------- | ---------- |
| `deploy/Install-PassReset.ps1`                              | modified | `d579046`  |
| `deploy/HUMAN-UAT-10-02.md`                                 | created  | `bedc341`  |
| `.planning/phases/10-operational-readiness/10-02-CHECKPOINT.md` | created  | (this file)|

## Resume signal

Resume the plan orchestrator with one of:

- `approved` — after scenarios A–D pass and the signed UAT doc is
  committed.
- `deferred — no VM` — to accept the Phase 7 deferral pattern.
