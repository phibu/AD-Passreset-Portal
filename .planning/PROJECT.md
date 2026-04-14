# PassReset — Project Context

**Initialized:** 2026-04-14 (brownfield)
**Current version:** v1.2.2
**Status:** Active development

## What This Is

PassReset is a self-service Active Directory password change portal. Users enter their current credentials and a new password; the backend authenticates against AD via LDAP(S) and performs the change.

**Stack (locked — no changes during this milestone chain):**
- Backend: ASP.NET Core 10 (`net10.0-windows`), System.DirectoryServices.AccountManagement, MailKit
- Frontend: React 19 + MUI 6 + Vite + TypeScript 5.8
- Hosting: IIS (Windows-only)
- CI: GitHub Actions on `windows-latest`

Full architecture, conventions, and build commands are captured in `./CLAUDE.md` (local-only, gitignored). The public face lives in `README.md` and `docs/`.

## Core Value

A reliable, secure, self-service password reset portal that fits into corporate AD environments without bespoke deployment engineering — usable in air-gapped/internal-CA environments, observable via SIEM, and safe against credential brute-force.

## Context

- **Prior rollback (2026-04-13):** A v2.0 effort focused on MSI packaging was rolled back. CLAUDE.md still contains stale `<!-- GSD:project-start -->` markers referring to that scope. The current v2.0 plan below is the authoritative one.
- **No automated tests yet:** CI validates via `tsc` + clean Release build only. Introducing a test foundation is an explicit v1.3 goal (QA-001).
- **Deployment:** PowerShell script (`deploy/Install-PassReset.ps1`) + zip artifact — MSI approach has been explicitly deferred/dropped.
- **Backlog source of truth:** `Todo.MD` (structured, committed).

## Requirements

### Validated (existing capabilities — brownfield)

- ✓ Self-service password change against AD (provider pattern: real AD + debug + lockout decorator) — existing
- ✓ Per-IP rate limiting (5 req / 5 min on POST `/api/password`) — existing
- ✓ Per-user portal lockout tracked in-memory (threshold + window) — existing
- ✓ HaveIBeenPwned breach check via `PwnedPasswordChecker` (k-anonymity API) — existing
- ✓ reCAPTCHA v3 bot prevention (optional) — existing
- ✓ Password strength meter (zxcvbn) + Levenshtein distance validation — existing
- ✓ SIEM integration: 10 event types via RFC 5424 syslog + optional email alerts — existing
- ✓ Password expiry notification background service (daily, group-scoped) — existing
- ✓ Fire-and-forget email notification on successful change (MailKit/SMTP) — existing
- ✓ Security headers (CSP, HSTS, X-Frame-Options, nosniff, Referrer-Policy, Permissions-Policy) — existing
- ✓ `/api/health` endpoint with AD connectivity check — existing
- ✓ Client settings flow: server → `GET /api/password` → `useSettings()` hook (single source) — existing
- ✓ Dark mode via `prefers-color-scheme`; MUI theme with teal primary (`#0b6366`) — existing
- ✓ PowerShell installer with config preservation + rollback (hardened in v1.2.2) — existing

### Active (v1.2.3 — Hotfix milestone, P1)

- [ ] **BUG-001** SMTP SSL handshake succeeds when relay presents internal-CA cert, without silent bypass
- [ ] **BUG-002** `E_ACCESSDENIED (0x80070005)` from AD min-pwd-age maps to `ApiErrorCode.PasswordTooRecentlyChanged` with user-friendly message
- [ ] **BUG-003** `Install-PassReset.ps1` preserves existing IIS AppPool identity on upgrade

### Active (v1.3.0 — UX + Quality milestone)

- [ ] **FEAT-001** Branding surfaces (company/portal name, helpdesk URL/email, usage text, logo + favicon) via `ClientSettings`
- [ ] **FEAT-002** Display effective AD password policy (min requirements), toggleable, default off, fails closed
- [ ] **FEAT-003** Clipboard protection — clear clipboard N seconds after password generator use (if still matches)
- [ ] **FEAT-004** HIBP breach status indicator on new-password blur, respecting `FailOpenOnPwnedCheckUnavailable`
- [ ] **QA-001** Test foundation — xUnit (backend) + Vitest/RTL (frontend), CI gates block on test failures

### Active (v2.0.0 — Platform evolution)

- [ ] **V2-001** Multi-OS support — research + PoC Docker image that performs a password change against a test AD without `System.DirectoryServices.AccountManagement`
- [ ] **V2-002** Local password protection DB (lithnet-style): operator-managed banned words + attempted-pwned lookup table; enforced even when stricter than AD policy
- [ ] **V2-003** Secure config storage — replace cleartext `appsettings.Production.json` secrets (DPAPI / Data Protection / Credential Manager / optional Key Vault adapter)

### Out of Scope

- **MSI packaging** — previously explored under a v2.0 MSI milestone (rolled back 2026-04-13); superseded by the hardened PowerShell installer.
- **Changing the tech stack** — React 19 / MUI 6 / ASP.NET Core 10 are locked for this milestone chain.
- **Password reset via email/SMS flow** — this portal is *change* only (user knows current password).
- **Identity federation / SSO adapters** — explicitly a direct-AD portal.

## Key Decisions

| Decision | Rationale | Outcome |
|---|---|---|
| Keep PowerShell installer; drop MSI | MSI path was rolled back; PS installer hardened in v1.2.2 | Accepted 2026-04-13 |
| v1.2.3 = bugs-only hotfix; v1.3 = features + tests in parallel | Ship P1 fixes fast; let QA-001 land alongside UX without blocking each other | Chosen 2026-04-14 |
| Coarse phase granularity + parallel plan execution | Fewer, broader phases fit the three-milestone structure; QA-001 runs parallel to FEAT work | Chosen 2026-04-14 |
| No tech stack changes this milestone chain | Reduce risk; features must fit current React 19 / MUI 6 / ASP.NET Core 10 | Locked |
| Balanced model profile (Sonnet) for agents | Good quality/cost for a mature brownfield project | Chosen 2026-04-14 |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd:transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd:complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-04-14 after initialization (brownfield, v1.2.2 baseline)*
