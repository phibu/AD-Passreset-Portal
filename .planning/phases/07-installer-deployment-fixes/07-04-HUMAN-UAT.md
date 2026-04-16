---
status: partial
phase: 07-04-installer-deployment-fixes
source: [07-04-SUMMARY.md]
started: 2026-04-16
updated: 2026-04-16
---

## Current Test

[awaiting operator UAT on a real Windows Server / IIS box with an existing PassReset install]

## Tests

### A. Same-version reconfigure — interactive (STAB-002)
expected: PassReset is installed at version `vX.Y.Z`. Re-run `.\Install-PassReset.ps1` with the same build (same version). The installer prints the detection block showing `Installed : vX.Y.Z` / `Incoming  : vX.Y.Z` and the note `Incoming version is the SAME as installed - this will RE-CONFIGURE, not upgrade`. The prompt reads **`Re-configure existing installation? [Y/N]`** (the word "upgrade" must NOT appear in the prompt). Answering `Y` logs `Reconfigure mode - skipping file mirror; existing publish folder preserved` and the app-pool / binding / config logic still re-runs. Site remains reachable.
result: [pending]

### B. Same-version reconfigure — `-Force` (STAB-002)
expected: Same setup as A. Run `.\Install-PassReset.ps1 -Force <other params>`. No interactive prompt. Installer logs `-Force specified - re-configuring without file mirror`. File mirror is skipped; downstream logic runs.
result: [pending]

### C. Upgrade preserves AppPool identity (STAB-003 regression)
expected: Existing PassReset AppPool is configured with `SpecificUser` identity (e.g., `DOMAIN\svc-passreset`). Deploy a higher-version build and run `.\Install-PassReset.ps1`. Upgrade path runs. Console output does **NOT** contain the string `Could not read existing AppPool identity`. Post-install verification: `Get-WebConfigurationProperty -PSPath 'IIS:\' -Filter "system.applicationHost/applicationPools/add[@name='PassReset']" -Name processModel.userName` returns the original `DOMAIN\svc-passreset` identity.
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
