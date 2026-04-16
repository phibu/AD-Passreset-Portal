---
phase: 07-installer-deployment-fixes
plan: 02
status: complete
requirements_addressed: [STAB-004]
completed: 2026-04-16
---

## What was built

STAB-004 consecutive-change pre-check. `PasswordChangeProvider.PerformPasswordChangeAsync`
now short-circuits with `ApiErrorCode.PasswordTooRecentlyChanged` (code 19) when the
user's `pwdLastSet` is newer than the domain `minPwdAge`. The message quotes elapsed,
policy, and remaining minutes so the UI can display an actionable wait time instead of
the previous `UnauthorizedAccessException` / generic crash.

## Key files

### Created
- `src/PassReset.Tests/PasswordProvider/PreCheckMinPwdAgeTests.cs` — 6 xUnit tests covering the pure evaluator.

### Modified
- `src/PassReset.PasswordProvider/PasswordChangeProvider.cs`
  - Call site: line 137 (immediately after `UserCannotChangePassword` check, before `ChangePasswordInternal`).
  - `PreCheckMinPwdAge(string)` — lines 654-689 (try/AD-read/fail-open).
  - `EvaluateMinPwdAge(DateTime, TimeSpan, DateTime)` — lines 691-707 (pure, `internal static` for unit tests).
- `src/PassReset.PasswordProvider/PassReset.PasswordProvider.csproj`
  - Added `<InternalsVisibleTo Include="PassReset.Tests" />` so the test assembly can exercise the static evaluator.

### Unchanged (defense-in-depth preserved per D-05)
- Existing `COMException` catch at ~line 485 with `E_ACCESSDENIED` / `ERROR_DS_CONSTRAINT_VIOLATION` mapping to `PasswordTooRecentlyChanged` — **byte-identical** to pre-change (only context lines near new call site differ in diff).
- Existing `PasswordTooYoung` (code 13) hours-based pre-check at ~:425-441 — zero diff in range.

## Deviations from plan

1. **Static-helper factoring**: the plan wrote one `PreCheckMinPwdAge` method containing
   both AD I/O and time math. I split the pure time logic into `internal static
   EvaluateMinPwdAge(lastSet, minAge, now)` so the policy can be unit tested without
   mocking `PrincipalContext`. Behavior is identical; the test surface is narrower.
2. **Warn-on-block logging**: the plan specified a `LogWarning` on reject; the agent's
   initial implementation only logged on exception. Added the "STAB-004 pre-check
   blocked consecutive change for {User}" warning in the reject path so the block is
   observable in logs.
3. **Known-Limitations.md no-op**: the plan said "delete stale entry if present". Search
   for `UnauthorizedAccessException`, `E_ACCESSDENIED`, `consecutive`, and `generic error`
   in `docs/Known-Limitations.md` returned **zero matches** (only one `minPwdAge` ref on
   line 27, which is the FGPP/PSO entry — unrelated to STAB-004). File left unchanged
   per plan Task 3 fallback.

## Acceptance evidence

- `grep -c "PreCheckMinPwdAge" PasswordChangeProvider.cs` → 2 (definition + call site).
- `grep -c "ApiErrorCode.PasswordTooRecentlyChanged" PasswordChangeProvider.cs` → 2 (pre-check + existing catch block).
- `grep -c "E_ACCESSDENIED" PasswordChangeProvider.cs` → 2 (catch block still intact).
- `dotnet build src/PassReset.sln --configuration Release` → **0 errors, 0 warnings**.
- `dotnet test src/PassReset.sln --configuration Release` → **87/87 pass** (6 new + 81 existing).
- Filtered run `--filter "FullyQualifiedName~PreCheckMinPwdAge"` → **6/6 pass**.

## Test names added

1. `UserWithinMinAge_ReturnsPasswordTooRecentlyChanged`
2. `UserOutsideMinAge_ReturnsNull`
3. `ExactlyAtBoundary_ReturnsNull`
4. `Message_QuotesElapsedPolicyAndRemainingMinutes`
5. `RoundingFloorsElapsedAndCeilsRemaining`
6. `RemainingMinutesClampedToAtLeastOne`

## Self-Check

- [x] Call-site inserted after auth + `UserCannotChangePassword`, before `ChangePasswordInternal`.
- [x] Uses `AcquirePrincipalContext()` (service-account-with-fallback) — no new credential path.
- [x] Fails open on any exception (logger.LogWarning, `return null`) so the existing COMException catch remains the floor.
- [x] `PasswordTooYoung` (code 13) block untouched.
- [x] COMException catch block untouched.
- [x] Build + full test suite green.

## Requirements addressed
- **STAB-004** — ROADMAP Phase 7 Success Criterion #4 (gh#36).
