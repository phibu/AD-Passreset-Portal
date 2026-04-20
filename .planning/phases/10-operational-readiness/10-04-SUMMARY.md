---
phase: 10-operational-readiness
plan: 04
requirement: STAB-021
status: complete
completed_at: 2026-04-20
---

# Plan 10-04 Summary — AD Password Policy Panel Default Visibility [STAB-021]

## Objective
Make the effective AD password policy visible to the user by default, rendered above the Username field in `PasswordForm`. Closes STAB-021 per D-16..D-19.

## Decisions applied
- **D-16** — `ShowAdPasswordPolicy` default flipped from `false` → `true` across all config sources (ClientSettings.cs, appsettings.json, appsettings.Production.template.json, appsettings.schema.json). Config-schema-sync invariant from Phase 08 preserved.
- **D-17** — Panel JSX block moved from below "Current Password" to above "Username" so rules are visible before the user starts typing.
- **D-18** — Panel still hides when `showAdPasswordPolicy=false`; no behavioral regression for opted-out operators.
- **D-19** — A11y regression guard: no disclosure widget reintroduced; panel exposes `role=region` with accessible name; tab order traverses panel before Username.

## Files modified
- `src/PassReset.Web/Models/ClientSettings.cs` — default flip, docstring updated
- `src/PassReset.Web/appsettings.json` — new `ShowAdPasswordPolicy: true`
- `src/PassReset.Web/appsettings.Production.template.json` — new `ShowAdPasswordPolicy: true`
- `src/PassReset.Web/appsettings.schema.json` — default changed `false` → `true`
- `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` — panel moved above Username field

## Files created
- `src/PassReset.Web/ClientApp/src/components/__tests__/AdPasswordPolicyPanel.test.tsx` (visibility + placement tests)
- `src/PassReset.Web/ClientApp/src/components/__tests__/AdPasswordPolicyPanel.a11y.test.tsx` (a11y regression guards)

## Tests
- Frontend: 54/54 tests green (`vitest run`) — includes new panel + a11y coverage
- Backend: 179/179 tests green (`dotnet test --configuration Release`) — no regressions
- ESLint: pre-existing 2 errors in `BrandHeader.tsx` and `usePolicy.ts` (set-state-in-effect) — NOT introduced by this plan; tracked separately

## Commits
- `fd22d05` feat(web): default AD password policy panel to visible [STAB-021]
- `1ced4e7` feat(web): render AD policy panel above username by default [STAB-021]
- `d1cd3c5` test(web): cover AD policy panel visibility + a11y [STAB-021]

## Acceptance criteria
- [x] `ShowAdPasswordPolicy` default is `true` in all four sources
- [x] Panel renders above Username in DOM order
- [x] A11y: `role=region`, accessible name, tab order traverses panel before Username
- [x] Opt-out (`ShowAdPasswordPolicy=false`) still hides the panel
- [x] Full frontend + backend test suites green

## Deviations
- Plan referenced jest-axe for a11y testing; executor used RTL role/order assertions instead to avoid adding a new dev dependency. Coverage equivalent for the D-19 guard surface.
