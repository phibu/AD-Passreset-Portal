# PassReset — Roadmap

**Milestone chain:** v1.2.3 ✅ → v1.3.0 ✅ → v1.3.1 ✅ → v1.3.2 ✅ → v2.0.0 (active)
**Granularity:** coarse
**Parallelization:** enabled
**Created:** 2026-04-14
**Last updated:** 2026-04-16

## Shipped Milestones

- ✅ **v1.2.3 Hotfix** (2026-04-14) — 3 P1 bugs fixed. See [`milestones/v1.2.3-ROADMAP.md`](milestones/v1.2.3-ROADMAP.md).
- ✅ **v1.3.0 Test Foundation + UX Features** (2026-04-15) — QA-001 + FEAT-001..004. See [`milestones/v1.3.0-ROADMAP.md`](milestones/v1.3.0-ROADMAP.md).
- ✅ **v1.3.1 AD Diagnostics** (2026-04-15) — BUG-004. See [`milestones/v1.3.1-ROADMAP.md`](milestones/v1.3.1-ROADMAP.md).
- ✅ **v1.3.2 Diagnostics Code Review Fixes** (2026-04-16) — WR-01..03 rollup on top of v1.3.1. See [`milestones/v1.3.2-ROADMAP.md`](milestones/v1.3.2-ROADMAP.md).

## Active Phases — v2.0.0

- [ ] **Phase 4: v2.0 Multi-OS PoC** — Research cross-platform path and produce Docker PoC
- [ ] **Phase 5: v2.0 Local Password DB** — Operator-managed banned-words + attempted-pwned lookup store
- [ ] **Phase 6: v2.0 Secure Config Storage** — Eliminate cleartext secrets from appsettings.Production.json

## Phase Details

### Phase 4: v2.0 Multi-OS PoC
**Goal**: A documented, evidence-backed decision on cross-platform viability, validated by a working Docker PoC against a test AD
**Depends on**: v1.3.0 (shipped)
**Parallel with**: None (findings gate Phases 5 and 6 design)
**Target release**: v2.0.0 (research deliverable; production migration may be deferred)
**Requirements**: V2-001
**Success Criteria** (what must be TRUE):
  1. A research document exists comparing `Novell.Directory.Ldap.NETStandard` (and alternatives) against the current `System.DirectoryServices.AccountManagement` usage, with a recommended path
  2. A Docker image builds from the repo and performs a successful password change against a test AD without `S.DS.AM`
  3. An explicit go/no-go decision on full Linux support is captured in `PROJECT.md` Key Decisions
  4. A provider abstraction boundary is identified (or confirmed sufficient) such that future cross-platform work doesn't require a rewrite
**Plans**: TBD

### Phase 5: v2.0 Local Password DB
**Goal**: Operators can enforce banned-word and attempted-pwned rules locally, independent of (and stricter than) AD policy
**Depends on**: Phase 4 (provider-abstraction findings inform integration shape)
**Parallel with**: Phase 6 (could overlap once Phase 4 lands, but coarse granularity keeps them sequential by default)
**Target release**: v2.0.0
**Requirements**: V2-002
**Success Criteria** (what must be TRUE):
  1. Operators can add and remove banned terms via a documented mechanism; changes take effect without code rebuild
  2. A local attempted-pwned lookup store exists and is consulted during password change; matches reject the change with a distinct `ApiErrorCode`
  3. Local rules are enforced even when AD would accept the password (strictly additive)
  4. Any borrowed logic (e.g., from lithnet/ad-password-protection) has a LICENSE-compatible integration documented in the repo
**Plans**: TBD

### Phase 6: v2.0 Secure Config Storage
**Goal**: Secrets in `appsettings.Production.json` are never stored as cleartext on disk by default, with a clear upgrade path for existing installs
**Depends on**: Phase 4 (cross-platform constraints shape mechanism choice — e.g., DPAPI is Windows-only)
**Parallel with**: Phase 5 (independent of V2-002 scope)
**Target release**: v2.0.0
**Requirements**: V2-003
**Success Criteria** (what must be TRUE):
  1. SMTP, reCAPTCHA, and LDAP credentials can be stored via a secure mechanism (DPAPI / ASP.NET Core Data Protection / Credential Manager / optional Key Vault adapter) chosen and documented
  2. A fresh install has no cleartext secrets on disk by default
  3. An existing v1.3.x install can upgrade to v2.0 and migrate its secrets following a documented procedure in `UPGRADING.md`
  4. `docs/Secret-Management.md` reflects the new default and documents fallback/override knobs
**Plans**: TBD

## Cross-Phase Dependencies

| From | To | Nature |
|---|---|---|
| Phase 4 | Phase 5 | Provider-abstraction decision informs local-DB integration point |
| Phase 4 | Phase 6 | Platform decision (Windows-only vs cross-platform) constrains secret-storage mechanism |

## Parallelism Map

- **Sequential default:** Phase 4 → Phase 5 → Phase 6
- Phases 5 and 6 *could* run in parallel once Phase 4 lands; coarse granularity keeps them sequential unless capacity allows.

## Progress

| Phase | Plans Complete | Status | Completed |
|---|---|---|---|
| 4. v2.0 Multi-OS PoC | 0/0 | Not started | — |
| 5. v2.0 Local Password DB | 0/0 | Not started | — |
| 6. v2.0 Secure Config Storage | 0/0 | Not started | — |

## Coverage

- Active v2.0 requirements: **3** (V2-001, V2-002, V2-003)
- Mapped: **3/3** ✓
- Orphans: **0**

---
*Last updated: 2026-04-16 (archived v1.3.2 milestone)*
