# Phase 14 — Hosting Modes (IIS / Windows Service / Console) — Design

**Status:** Approved (brainstorming complete 2026-04-22)
**Milestone:** v2.0.0 (part of the multi-OS + IIS-independence track)
**Precedes:** Phase 15 (Linux parity), Phase 16 (Docker)
**Depends on:** Phase 13 (admin UI + Data Protection) shipped on master `2c25bad`

---

## Goal

Make IIS optional for PassReset. v2.0 installs on Windows support three hosting modes:

1. **IIS** — current behavior, preserved
2. **Windows Service** — Kestrel self-hosted; Kestrel terminates TLS directly
3. **Console** — foreground Kestrel for dev / debugging

Operators pick a mode at install time. Upgrades default to the current mode (IIS stays on IIS). Migration IIS → Service is explicit, gated by a preflight dry-run that leaves IIS untouched if preconditions fail.

This phase is Windows-only. Linux support (systemd, LDAP-only provider) is Phase 15. Docker publishing is Phase 16.

---

## Non-goals

- Linux / macOS support. Neither the Service registration code, the TLS cert loading path, nor the installer changes in this phase need to run anywhere except Windows.
- Docker image. Separate phase.
- Rewriting the admin UI, config schema, or password-change provider chain.
- Changing how Data Protection key storage works (Phase 13 established `<install-dir>\keys\` with DPAPI; this phase only adds a Service-mode verification that DPAPI keys are readable under the service identity).
- A full rewrite of `docs/IIS-Setup.md`. It becomes a stub that redirects to the new `docs/Deployment.md`.

---

## Architecture overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Install-PassReset.ps1                    │
│                                                             │
│   -HostingMode IIS | Service | Console   (prompts if empty) │
│   -CertThumbprint X  |  -PfxPath Y -PfxPassword Z           │
│   -ServiceAccount ...  (Service mode, optional)             │
│                                                             │
│   ┌────────── preflight dry-run ──────────┐                 │
│   │ cert exists? port free? acct valid?   │                 │
│   │  → fail-fast before any IIS teardown  │                 │
│   └──────────────────────────────────────┘                  │
└─────────┬─────────────────┬─────────────────┬───────────────┘
          │                 │                 │
          ▼                 ▼                 ▼
     ┌────────┐        ┌──────────┐      ┌──────────┐
     │  IIS   │        │ Windows  │      │ Console  │
     │ site + │        │ Service  │      │ foregrnd │
     │ AppPool│        │(virtual/ │      │  kestrel │
     │        │        │ domain)  │      │          │
     └────┬───┘        └────┬─────┘      └────┬─────┘
          │                 │                 │
          ▼                 ▼                 ▼
       aspNetCore      Kestrel directly   Kestrel directly
       module          binds 443 with     binds 5001 (dev)
       fronts          cert from store
       Kestrel         or PFX file
       TLS at IIS      TLS at Kestrel     no TLS by default
```

### Two new code surfaces in `PassReset.Web`

1. **`Kestrel:HttpsCert` options binding** (Service mode only; IIS ignores)
   - Two mutually exclusive inputs:
     - `Thumbprint` (+ optional `StoreLocation`, default `LocalMachine`, `StoreName`, default `My`)
     - `PfxPath` (+ `PfxPassword`)
   - Validated at startup via `IValidateOptions<KestrelHttpsCertOptions>`
   - Loaded into `KestrelServerOptions.ConfigureHttpsDefaults` / `.Listen(IPAddress.Any, 443, https => https.UseHttps(cert))`

2. **Hosting-mode detection + startup logging**
   - `Program.cs` detects whether running under IIS (`WindowsServiceLifetime.IsWindowsService()` vs. `WebHostDefaults.ServerUrlsKey` presence) or as a Service or console
   - Logs `Information` at startup: `"PassReset starting. HostingMode: {Mode}"`
   - No runtime behavior change based on mode — just observability

### Installer changes (`deploy/Install-PassReset.ps1`)

- New param: `[ValidateSet('IIS','Service','Console')] $HostingMode`
- Prompt if unset (fresh install); default to current detected mode (upgrade)
- Branch on mode:
  - **IIS**: existing code path, unchanged except minor cleanup to support new top-level branching
  - **Service**: preflight → tear down IIS if migrating → `sc.exe create PassReset …` (or the PowerShell `New-Service` equivalent) with `DelayedAutoStart=true`, `DependsOn=""`, identity from operator prompt
  - **Console**: installs files only, no service registration, no IIS site — informational output tells the operator how to run the binary manually
- New param: `[string] $ServiceAccount` (Service mode only) — if missing + Service mode: prompts virtual/domain. If domain, prompts for SecureString password.
- New param: `[string] $PfxPath` + `[securestring] $PfxPassword` (Service mode only, mutually exclusive with `-CertThumbprint`)
- Preflight dry-run (Service mode, fresh or migration):
  1. Resolve cert (thumbprint → store lookup OR PFX path → file exists + decrypt works)
  2. Verify port 443 (or configured port) is free
  3. Verify service account: virtual → always valid; domain → `net user /domain` or `Test-ADAccount` reachability
  4. All-or-nothing: if any step fails, abort, IIS untouched (migration case) or nothing installed (fresh case)

### Windows Service details

| Aspect | Value |
|---|---|
| Service name | `PassReset` |
| Display name | `PassReset Password Reset Portal` |
| Description | `Self-service Active Directory password reset portal.` |
| Startup type | `Automatic (Delayed)` — waits for AD / network readiness |
| Dependencies | none (Kestrel has no hard dependencies; AD reachability handled at runtime) |
| Identity (default) | `NT SERVICE\PassReset` — virtual service account |
| Identity (override) | domain account via `-ServiceAccount DOMAIN\user` + SecureString prompt |
| Failure actions | (future phase) — for now, SCM default |

Installer uses `Microsoft.Extensions.Hosting.WindowsServices.UseWindowsService()` in `Program.cs` so the same binary works as a console app or service.

### TLS cert config surface

In `appsettings.Production.json` (Service mode only; IIS mode ignores):

```jsonc
"Kestrel": {
  "HttpsCert": {
    // Option A: Windows cert store lookup
    "Thumbprint": "ABCDEF0123456789...",
    "StoreLocation": "LocalMachine",   // default; CurrentUser not supported in Service mode
    "StoreName": "My",                  // default

    // Option B: PFX file (mutually exclusive with Thumbprint)
    "PfxPath": null,
    "PfxPassword": null                 // stored via Phase 13 SecretStore in practice
  },
  "Endpoints": {                        // optional; default is https://*:443
    "Https": { "Url": "https://*:443" }
  }
}
```

Validator rejects both Thumbprint and PfxPath being set, or neither (when Service mode is active). `PfxPassword` lives in `secrets.dat` (Phase 13 mechanism) when admin UI is enabled; a migration for existing operators who want to move from cleartext → encrypted will be part of the `docs/Deployment.md` upgrade guide.

### Admin UI (Phase 13) interaction

Phase 13 added a loopback Kestrel listener on port 5010 for the admin UI. In Service mode that keeps working (one process, multiple Kestrel endpoints). In IIS mode the ASP.NET Core module handles the main HTTP listener, but the admin UI's secondary loopback listener is still started by our `Program.cs`.

**Risk: IIS + Phase 13 admin UI interaction.** The ASP.NET Core module expects one Kestrel listener. The admin UI starts a second loopback listener via `WebHost.ConfigureKestrel(opts => opts.Listen(...))`. This was shipped in Phase 13 but never verified under IIS — Phase 13 smoke-tested on `dotnet run` only. This phase **must verify** the admin UI works under IIS and document any gotchas. Fallback: if IIS + secondary listener genuinely doesn't work, disable admin UI in IIS mode (with a clear operator message) and document the workaround (switch to Service mode to use the admin UI).

---

## Observability

**Startup log (Serilog, `Information`):**
```
PassReset starting. HostingMode: Service. Process: PassReset.exe.  
Endpoint: https://*:443 (cert thumbprint: A1B2...). Admin UI: http://127.0.0.1:5010/admin (disabled by default).
```

**Runtime: no new `/api/health` fields.** Hosting mode is an install-time fact; runtime endpoint stays minimal (AD, SMTP, expiry service checks unchanged).

**Failure surfaces:**
- **Service mode:** SCM is the source of truth. Service fails to start → Windows Event Log under *System* source shows SCM event 7000/7031/7034; operator consults `services.msc`. `StartupValidationFailureLogger` still writes to the PassReset-named Event Log source for application-layer failures (validation, config), but Service-start failures are SCM's job.
- **IIS mode:** no change — ASP.NET Core Module writes to the Event Log on worker crash; our existing logging stays.
- **Console mode:** stderr / Serilog console sink.

---

## Upgrade paths

### v1.x (IIS) → v2.0 (IIS — stay)

```powershell
.\Install-PassReset.ps1 -Force -CertThumbprint <same-as-before>
# Installer auto-detects IIS site, prompts default = Stay on IIS
# Refreshes binaries + schema-validated config sync; no hosting-mode change
```

Tests: existing IIS regression tests pass.

### v1.x (IIS) → v2.0 (Windows Service — migrate)

```powershell
.\Install-PassReset.ps1 -Force -HostingMode Service -CertThumbprint <thumbprint>
# 1. Preflight:
#    ✓ Cert in LocalMachine\My
#    ✓ Port 443 not bound by another process (IIS's binding counts as owned by PassReset)
#    ✓ Service account valid
# 2. If preflight ok: tear down IIS site + AppPool, register service, start
# 3. If preflight fails: abort, IIS untouched, operator sees reason
```

Docs walk operator through the migration in `docs/Deployment.md#migrating-iis-to-service`.

### v2.0 fresh install

```powershell
.\Install-PassReset.ps1
# Prompts for:
#   - HostingMode: [I]IS / [S]ervice / [C]onsole
#   - CertThumbprint (or -PfxPath)
#   - If Service: identity ([V]irtual / [D]omain)
```

### Rollback (if v2.0 Service-mode upgrade goes wrong)

Because preflight aborts before teardown, a mid-migration failure never leaves the operator in a broken state. If a service that was successfully installed turns out to be misconfigured, `.\Uninstall-PassReset.ps1` removes it cleanly, then `.\Install-PassReset.ps1 -HostingMode IIS` reinstalls the prior hosting mode.

---

## File structure

### New files

| Path | Purpose |
|---|---|
| `src/PassReset.Web/Configuration/KestrelHttpsCertOptions.cs` | Options record for TLS cert (Thumbprint OR PfxPath) |
| `src/PassReset.Web/Models/KestrelHttpsCertOptionsValidator.cs` | Validates mutual exclusion + Service-mode requirement |
| `src/PassReset.Web/Services/Hosting/HostingMode.cs` | `enum HostingMode { Iis, Service, Console }` |
| `src/PassReset.Web/Services/Hosting/HostingModeDetector.cs` | Detects current hosting mode for startup logging |
| `src/PassReset.Tests.Windows/Configuration/KestrelHttpsCertOptionsValidatorTests.cs` | xUnit — validator behavior |
| `src/PassReset.Tests.Windows/Services/Hosting/HostingModeDetectorTests.cs` | xUnit — detection logic |
| `deploy/Install-PassReset.Tests.ps1` | Pester tests for installer param handling + preflight dry-run (new file — Pester introduced by this phase) |
| `docs/Deployment.md` | Consolidated deployment guide (IIS / Service / Console / cert config / migration) |

### Modified files

| Path | Change |
|---|---|
| `src/PassReset.Web/Program.cs` | Add `.UseWindowsService()`, Kestrel cert configuration (Service mode), hosting-mode startup log |
| `src/PassReset.Web/PassReset.Web.csproj` | Add `Microsoft.Extensions.Hosting.WindowsServices` reference |
| `src/PassReset.Web/appsettings.json` | Add `Kestrel:HttpsCert` block with null defaults |
| `src/PassReset.Web/appsettings.Production.template.json` | Same, with operator-facing comments in `docs/Deployment.md` |
| `deploy/Install-PassReset.ps1` | `-HostingMode` param, prompts, preflight, Service registration, IIS branch unchanged structurally |
| `deploy/Uninstall-PassReset.ps1` | Handle Service uninstall in addition to existing IIS teardown; prompt if ambiguous |
| `docs/IIS-Setup.md` | Reduced to a redirect stub pointing at `docs/Deployment.md#iis-mode` |
| `docs/appsettings-Production.md` | Add `Kestrel:HttpsCert` section |
| `CHANGELOG.md` | Phase 14 entry |
| `CLAUDE.md` | New `KestrelHttpsCertOptions` under "Configuration keys to know" |

---

## Tests

### Unit tests (xUnit)

- **`KestrelHttpsCertOptionsValidatorTests`** (~6 tests): valid thumbprint, valid PFX, both set (fail), neither set in Service mode (fail), neither set in IIS mode (ok — IIS terminates), invalid store location
- **`HostingModeDetectorTests`** (~3 tests): detects Service when `WindowsServiceHelpers.IsWindowsService()` returns true, detects IIS when `ASPNETCORE_IIS_HTTPAUTH` env var set, falls back to Console

### Pester tests (installer)

- **`Install-PassReset.Tests.ps1`** — new file; first Pester coverage in the repo:
  - `-HostingMode` param validation (accepts IIS/Service/Console only)
  - Preflight dry-run returns early with clear errors when cert missing, port bound, identity invalid
  - Prompt flow when params missing
  - Upgrade detection: IIS-hosted v1.x → prompt default = "Stay on IIS"

### Integration / smoke

- **`dotnet run` (Console mode):** regression test that Phase 13 admin UI still works (previously verified; re-run under new Program.cs)
- **Manual smoke** (per `docs/Deployment.md`): install Service mode on a test Windows Server 2019, verify `https://host/` returns the password form, verify `services.msc` shows delayed-auto-start, verify `Stop-Service PassReset; Start-Service PassReset` works
- **Admin UI + IIS smoke:** install IIS mode with admin enabled, verify `http://localhost:5010/admin` reachable from the server console — or, if it's not, confirm the documented workaround (disable admin UI when IIS).

---

## Risks

| # | Risk | Mitigation |
|---|---|---|
| 1 | DPAPI keys + virtual service account identity | Phase 13 uses `ProtectKeysWithDpapi()` which defaults to user-scope. `NT SERVICE\PassReset` has a user profile, but service virtualization may make DPAPI keys non-portable between restarts. Phase 14 adds a verification step in the Service install flow: load + decrypt a test secret after first start. If that fails, fall back to machine-scoped protection via `ProtectKeysWithDpapi(protectToLocalMachine: true)` — documented in `docs/Deployment.md`. |
| 2 | IIS + Phase 13 admin UI secondary listener | Never tested under IIS. Two outcomes: (a) it works and we document it; (b) it doesn't and we disable admin UI in IIS mode with a clear operator message, documenting "to use admin UI, switch to Service mode". Phase 14 acceptance criteria includes verifying one way or the other. |
| 3 | Port 443 already bound by IIS during migration | Preflight sees IIS's binding as "not free". Solution: preflight knows to check "is the binding owned by *this* PassReset IIS site?" — if yes, treat as available (will be freed during teardown). If owned by another IIS site / process, fail. |
| 4 | Pester is a new dep | Team convention check — Pester shipped with Windows PowerShell but installs need a modern version. Script must gracefully report "install Pester 5.x or skip test suite" if not present. CI runner `windows-latest` has it pre-installed. |
| 5 | Service account SecureString prompt in unattended install | `-ServicePassword` param accepts a SecureString; CI / automation passes it via `ConvertTo-SecureString -AsPlainText -Force` (documented caveat). Virtual account path remains the recommended CI-friendly default. |
| 6 | Windows Service + appsettings.Production.json hot-reload | IIS reloads appsettings on file change. Service mode with `.UseWindowsService()` still uses the default `ReloadOnChange=true` configuration source, so this is a non-issue — but documented in `docs/Deployment.md` for operator clarity. |

---

## Success criteria

1. Fresh install on clean Windows Server 2019 with `-HostingMode Service -CertThumbprint X` produces a running PassReset service reachable at `https://host/` — no IIS installed, no reverse proxy.
2. Fresh install with `-HostingMode IIS -CertThumbprint X` produces current-behavior IIS site (regression test passes).
3. Upgrade of a v1.x IIS install with no hosting flag stays on IIS, no disruption.
4. Migration IIS → Service: operator runs `Install-PassReset.ps1 -Force -HostingMode Service -CertThumbprint X`, preflight passes, IIS torn down, service registered + started, new TLS binding live.
5. Service mode: stop / start / restart via `services.msc` works; SCM shows healthy status after delayed start; AD reachable.
6. Console mode: `dotnet run --project src/PassReset.Web` works unchanged.
7. `docs/Deployment.md` exists and covers all three modes + migration + cert config.
8. Admin UI (Phase 13): either verified working under IIS OR disabled with a documented operator message.
9. Serilog startup log contains hosting mode as `Information` event.
10. Windows Event Log: application-layer failures still write to PassReset source; Service-start failures are SCM's responsibility (no duplicate noise).

---

## Out of scope (deferred)

- Linux systemd unit — **Phase 15**
- Docker image + multi-arch CI publish — **Phase 16**
- New ROADMAP doc (retire `.planning/ROADMAP.md`) — **Phase 17** (tiny, can run in parallel)
- Follow-ups from Phase 13 code review (admin-action SIEM audit, bootstrap DP provider refactor)
- reCAPTCHA private-key migration from appsettings → secrets.dat (orthogonal; existing STAB-017 env-var path still works)

---

## Open questions (none blocking)

- DPAPI scope for virtual service accounts — will be answered by the preflight verification in the installer; result drives a doc note, not a scope change.
- Pester version pinning — verify `windows-latest` runner's version at implementation time, pin to `>= 5.3.0` in the test script.

---

*End of Phase 14 design.*
