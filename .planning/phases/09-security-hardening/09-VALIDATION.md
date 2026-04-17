---
phase: 9
slug: security-hardening
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-17
---

# Phase 9 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (backend) + Vitest (frontend, not exercised this phase) |
| **Config file** | `src/PassReset.Tests/PassReset.Tests.csproj` (existing) |
| **Quick run command** | `dotnet test src/PassReset.sln --configuration Release --filter "FullyQualifiedName~PassReset.Tests" --nologo` |
| **Full suite command** | `dotnet test src/PassReset.sln --configuration Release` |
| **Estimated runtime** | ~45–90 seconds (backend suite; grows with STAB-014 WAF tests) |

---

## Sampling Rate

- **After every task commit:** Run quick run command (class-scoped filter where possible, e.g. `--filter "FullyQualifiedName~RateLimitAndRecaptchaTests"`)
- **After every plan wave:** Run full suite command
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 09-01-01 | 01 | 1 | STAB-013 | T-9-01 (enumeration) | Production responses collapse InvalidCredentials + UserNotFound to Generic (0); SIEM remains granular | unit | `dotnet test --filter "FullyQualifiedName~GenericErrorMappingTests"` | ❌ W0 | ⬜ pending |
| 09-02-01 | 02 | 2 | STAB-014 | T-9-02 (abuse) | Rate limit returns 429 + emits SIEM RateLimitExceeded on 6th request in window | integration | `dotnet test --filter "FullyQualifiedName~RateLimitAndRecaptchaTests.RateLimit"` | ❌ W0 | ⬜ pending |
| 09-02-02 | 02 | 2 | STAB-014 | T-9-02 (abuse) | reCAPTCHA enabled path rejects invalid token + emits SIEM RecaptchaFailed; disabled path accepts missing token | integration | `dotnet test --filter "FullyQualifiedName~RateLimitAndRecaptchaTests.Recaptcha"` | ❌ W0 | ⬜ pending |
| 09-03-01 | 03 | 1 | STAB-015 | T-9-03 (audit gap) | AuditEvent DTO has only allowlisted fields (no Password/Token field compiles) | unit | `dotnet test --filter "FullyQualifiedName~AuditEventRedactionTests"` | ❌ W0 | ⬜ pending |
| 09-03-02 | 03 | 1 | STAB-015 | T-9-03 (audit gap) | SiemSyslogFormatter emits valid RFC 5424 SD-ELEMENT with configured SdId; escape rules applied for `]`, `"`, `\` | unit | `dotnet test --filter "FullyQualifiedName~SiemSyslogFormatterTests.StructuredData"` | ❌ W0 | ⬜ pending |
| 09-04-01 | 04 | 1 | STAB-016 | T-9-04 (plain-HTTP) | HSTS header + UseHttpsRedirection still active when `WebSettings.EnableHttpsRedirect=true` | integration | `dotnet test --filter "FullyQualifiedName~HttpsRedirectionTests"` | ❌ W0 | ⬜ pending |
| 09-04-02 | 04 | 2 | STAB-016 | T-9-04 (plain-HTTP) | Installer `Test-HttpsBinding` warns when HTTPS binding missing; accepts when present | manual+pester | `pwsh -Command ". ./deploy/tests/Test-HttpsBinding.Tests.ps1"` (or documented manual steps if Pester absent) | ❌ W0 | ⬜ pending |
| 09-05-01 | 05 | 1 | STAB-017 | T-9-05 (secret leak) | Env-var `SmtpSettings__Password` overrides appsettings value at bind time | integration | `dotnet test --filter "FullyQualifiedName~EnvironmentVariableOverrideTests"` | ❌ W0 | ⬜ pending |
| 09-05-02 | 05 | 2 | STAB-017 | — | Docs (`Secret-Management.md`, `IIS-Setup.md`, `CONTRIBUTING.md`) contain exact `dotnet user-secrets` + `appcmd` commands | docs grep | `grep -q "dotnet user-secrets set" docs/Secret-Management.md && grep -q "appcmd set config" docs/IIS-Setup.md` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `src/PassReset.Tests/Web/GenericErrorMappingTests.cs` — STAB-013 unit tests over PasswordController's error collapse path
- [ ] `src/PassReset.Tests/Web/RateLimitAndRecaptchaTests.cs` — STAB-014 WebApplicationFactory<Program> integration tests (4 scenarios)
- [ ] `src/PassReset.Tests/Web/HttpsRedirectionTests.cs` — STAB-016 HSTS + redirect regression coverage
- [ ] `src/PassReset.Tests/Web/EnvironmentVariableOverrideTests.cs` — STAB-017 binding precedence test
- [ ] `src/PassReset.Tests/Services/AuditEventRedactionTests.cs` — STAB-015 compile-time + runtime redaction proof
- [ ] `src/PassReset.Tests/Services/SiemSyslogFormatterTests.cs` (extend) — STAB-015 structured-data + SdId tests
- [ ] `deploy/tests/Test-HttpsBinding.Tests.ps1` — optional Pester scaffold for STAB-016 installer check (or manual-verification fallback)

*xUnit v3 + coverlet framework already installed — no new dependency. Pester is optional (manual verification path acceptable if install is out of scope).*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Install-PassReset.ps1 binding warn fires against a real IIS site | STAB-016 | Requires IIS + elevation; WebAdministration module not mockable in CI | (1) On a test VM, create an IIS site with only an HTTP binding. (2) Run `.\deploy\Install-PassReset.ps1 -SiteName <name> -Force`. (3) Confirm the `Write-Warn` banner mentions the missing HTTPS binding and script does NOT abort. |
| AppPool environment variable precedence in real IIS | STAB-017 | Requires IIS AppPool with env-var set via appcmd | (1) On test VM: `%systemroot%\system32\inetsrv\appcmd set config -section:applicationPools "/[name='PassResetPool'].environmentVariables.[name='SmtpSettings__Password',value='FromEnv']"`. (2) Recycle AppPool. (3) Trigger an SMTP send and confirm the env-var value was used (inspect SIEM / logs). |
| End-to-end production error collapse over HTTPS | STAB-013 | Requires ASPNETCORE_ENVIRONMENT=Production in a published build on IIS | (1) Publish in Release. (2) Set `ASPNETCORE_ENVIRONMENT=Production` on the AppPool. (3) Submit a bad username via the form; confirm response body `errors[0].code == 0` (Generic) and SIEM log shows `UserNotFound`. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
