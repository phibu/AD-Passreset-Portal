---
phase: 10-operational-readiness
plan: 03
subsystem: ci-security
tags: [ci, security, deps]
requires: []
provides: [security-audit-ci-gate, security-allowlist-schema]
affects: [.github/workflows/tests.yml]
tech-stack:
  added: []
  patterns: [ci-security-gate, allowlist-expiration]
key-files:
  created:
    - deploy/security-allowlist.json
    - docs/Security-Audit-Allowlist.md
  modified:
    - .github/workflows/tests.yml
decisions:
  - Ship security-audit as a parallel job inside tests.yml (per D-13) rather than a separate security.yml workflow.
  - Gate on high + critical only; moderate/low print as warnings (per D-12).
  - Allowlist with ISO-8601 expires field enforced by PowerShell date comparison; expired entries silently fall through as unfixed.
  - Parse dotnet list output via Select-String + regex GHSA-[a-z0-9-]+ because `--format json` is not supported (R-02).
  - Wrap npm audit in `2>&1 | Out-String` and use explicit `exit 1`; do not rely on npm audit's own exit code (Pitfall 4).
metrics:
  duration: ~6 minutes
  completed: 2026-04-20
requirements_addressed: [STAB-020]
---

# Phase 10 Plan 03: CI Security Gate (STAB-020) Summary

Add a parallel `security-audit` job to `.github/workflows/tests.yml` that runs `npm audit` + `dotnet list package --vulnerable --include-transitive` on every push/PR, fails on high+critical findings, and reads a checked-in allowlist with 90-day expiration.

## What Changed

### Created

- **`deploy/security-allowlist.json`** — JSON allowlist with `_readme` (policy note) and empty `advisories: []` array. Schema: `{ id, rationale, expires (ISO date), scope: "npm"|"nuget" }` per entry.
- **`docs/Security-Audit-Allowlist.md`** — Operator guide covering purpose, entry schema, 90-day expiration policy, renewal flow, relationship to Dependabot/CodeQL (independent per D-15), worked JSON example, and commit conventions.

### Modified

- **`.github/workflows/tests.yml`** — Appended `security-audit` job (parallel to `tests`, no `needs:`). Steps:
  1. Checkout + .NET 10 + Node 22 setup (mirrors `tests` job).
  2. `npm ci` in ClientApp; `dotnet restore` at solution root.
  3. `npm audit --json` → filter severity ∈ {high, critical} → subtract allowlisted IDs (scope=npm, expires > today) → `exit 1` if any unsuppressed remain. Moderate/low reported via `Write-Warning`.
  4. `dotnet list ... --vulnerable --include-transitive` → text-parse via regex `GHSA-[a-z0-9\-]+` on lines matching `(High|Critical)` → subtract allowlisted IDs (scope=nuget, expires > today) → `exit 1` if unsuppressed.
  5. Post-audit summary step (`if: always()`) writes to `$GITHUB_STEP_SUMMARY`.

Existing `tests` job and the `ci.yml → tests.yml` workflow_call chain are untouched — the new job runs automatically via the existing invocation.

## Commits

| Task | Commit    | Subject                                                                       |
| ---- | --------- | ----------------------------------------------------------------------------- |
| 1    | `8257d45` | feat(ci): add STAB-020 security-audit allowlist + operator docs               |
| 2    | `77e38df` | ci(security): add security-audit job for npm + dotnet vulnerability gating [STAB-020] |

## Verification Performed

- `python -c "import yaml; yaml.safe_load(open('.github/workflows/tests.yml'))"` → exits 0; parsed jobs: `['tests', 'security-audit']`.
- `(Get-Content deploy/security-allowlist.json -Raw | ConvertFrom-Json).advisories.Count` → `0`.
- Confirmed `security-audit` has no `needs:` key (runs parallel with `tests`).
- Acceptance-criteria grep counts:
  - `security-audit:` → 1
  - `npm audit --json` → 1
  - `dotnet list src/PassReset.sln package --vulnerable --include-transitive` → 1
  - `security-allowlist.json` → 3 (≥ 2 required)
  - `GITHUB_STEP_SUMMARY` → 4
  - `continue-on-error` → 0 (absent, as required by D-14)
  - `exit 1` → 2 (one per audit step)

## Deviations from Plan

None — plan executed exactly as written.

## Threat Flags

None — no new security-relevant surface beyond what the threat model already enumerates (T-10-03-01..06 all addressed by the implementation).

## Known Stubs

None.

## Follow-ups (non-blocking)

- Optional manual spot-check per plan verification note: open a test PR with an intentionally-seeded high-severity advisory to observe the gate fail end-to-end (VALIDATION 10-03-01). Not blocking.

## Self-Check: PASSED

- `deploy/security-allowlist.json` — FOUND
- `docs/Security-Audit-Allowlist.md` — FOUND
- `.github/workflows/tests.yml` — FOUND (modified, contains `security-audit` job)
- Commit `8257d45` — FOUND in `git log`
- Commit `77e38df` — FOUND in `git log`
