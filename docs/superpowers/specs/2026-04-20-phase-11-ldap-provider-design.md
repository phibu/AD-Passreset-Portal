---
title: Phase 11 — Cross-Platform LDAP Password Provider
date: 2026-04-20
status: approved
milestone: v2.0.0
phase: 11
target_release: v2.0-alpha1
depends_on: [v1.4.2]
supersedes_scope: Multi-OS PoC (original Phase 11 scope — now decomposed across Phases 11–15)
---

# Phase 11 — Cross-Platform LDAP Password Provider

## Purpose

Build a `net10.0` cross-platform implementation of `IPasswordChangeProvider` using `System.DirectoryServices.Protocols` (`LdapConnection`), so PassReset can run on Linux (and eventually Docker, macOS for dev) while preserving the existing `System.DirectoryServices.AccountManagement`-based provider byte-for-byte on Windows.

This is the first phase of the **v2.0 Multi-OS** milestone. Hosting, packaging, CI, and docs follow in Phases 12–15.

## Decomposition context

v2.0 was originally a single Phase 11 ("Multi-OS PoC"). After brainstorming it decomposed into:

| Phase | Scope | Ships as | Est. |
|-------|-------|----------|------|
| **11** | **Provider rewrite** (this spec) — cross-platform LDAP provider. Byte-for-byte parity with Windows provider. Tests pass on Windows + Linux. | v2.0-alpha1 | 3 weeks |
| 12 | Kestrel-direct hosting replaces IIS on Windows; systemd unit on Linux. | v2.0-alpha2 | 2 weeks |
| 13 | nginx + Apache sample configs; Kestrel-direct option on Windows. | v2.0-beta1 | 1 week |
| 14 | Packaging: `.deb`, `.rpm`, Docker image, Windows zip with `sc.exe`-based installer. | v2.0-rc1 | 2 weeks |
| 15 | Linux CI runner, cross-platform test matrix, rewritten operator docs. | v2.0.0 | 2 weeks |

The previously-numbered Phase 14 (Web Admin Config UI) is re-sequenced to after the new Phase 15; roadmap renumbering happens during `/gsd-plan-phase 11` prep. Web Admin Config UI itself is unchanged — only its position in the queue moves.

## Non-goals (explicit YAGNI)

- ❌ Cross-platform AD binding via Kerberos / SSSD. Phase 11 is service-account-bind only.
- ❌ Connection pooling across requests. Password change is low-frequency; one connection per request.
- ❌ Multi-domain forest support. Existing constraint; not this phase's problem.
- ❌ Changes to the Windows provider. `PasswordChangeProvider` stays byte-for-byte identical. Zero regression for Windows operators.
- ❌ `UserCannotChangePassword` ACE inspection on Linux. Ldap provider treats everyone as "can change password"; AD's server-side rejection on the modify itself handles policy enforcement. Less specific error message is the accepted tradeoff. Logged as a warning at startup on Linux.
- ❌ Hosting / reverse proxy / packaging / CI infrastructure changes. Those are Phases 12–15.

## Architecture

### Project structure — before and after

**v1.4.2 (today):**
```
PassReset.Common/             — net10.0           (platform-neutral contracts)
PassReset.PasswordProvider/   — net10.0-windows   (AccountManagement-based)
PassReset.Web/                — net10.0-windows   (ASP.NET Core host + React)
PassReset.Tests/              — net10.0-windows
```

**v2.0-alpha1 (after Phase 11):**
```
PassReset.Common/                   — net10.0           (unchanged)
PassReset.PasswordProvider/         — net10.0-windows   (AccountManagement — UNCHANGED behavior)
PassReset.PasswordProvider.Ldap/    — net10.0           (NEW — LdapConnection-based, works everywhere)
PassReset.Web/                      — net10.0           (DROPS -windows TFM; conditional ref to Windows provider)
PassReset.Tests/                    — net10.0           (cross-platform: web + contract + Ldap unit)
PassReset.Tests.Windows/            — net10.0-windows   (NEW — Windows-provider-specific unit tests)
```

### Key wiring decisions

1. **`PassReset.Web` targets plain `net10.0`.** References to the Windows provider live behind a csproj conditional:

   ```xml
   <ItemGroup Condition="'$(OS)' == 'Windows_NT'">
     <ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />
     <DefineConstants>$(DefineConstants);WINDOWS_PROVIDER</DefineConstants>
   </ItemGroup>
   ```

   Linux / Docker builds compile without the Windows provider; `#if WINDOWS_PROVIDER` guards the wiring block in `Program.cs`.

2. **Provider selection lives in `Program.cs` at startup.** A new enum config:

   ```csharp
   public enum ProviderMode { Auto, Windows, Ldap }
   ```

   - `Auto` (default) → Windows provider on `OperatingSystem.IsWindows()`, else Ldap.
   - `Windows` → Windows provider always. Fails fast via `IValidateOptions` on non-Windows platforms.
   - `Ldap` → Ldap provider always (useful for forcing the Linux code path on Windows in staging).

3. **DI registration is fully conditional at compile time.** On Linux, the Windows provider doesn't exist in the binary at all.

   ```csharp
   var mode = EffectiveProviderMode(config);
   #if WINDOWS_PROVIDER
   if (mode == ProviderMode.Windows)
   {
       services.AddSingleton<IPasswordChangeProvider, PasswordChangeProvider>();
   }
   else
   #endif
   {
       services.AddSingleton<IPasswordChangeProvider, LdapPasswordChangeProvider>();
   }
   ```

4. **The lockout decorator, HIBP checker, SIEM service, email service, rate limiter, and every controller are unchanged.** They depend on `IPasswordChangeProvider` — which is already platform-neutral.

### New LDAP provider — behavioral equivalence map

| Current Windows provider (AccountManagement) | New Ldap provider (Protocols) |
|---|---|
| `PrincipalContext(ContextType.Domain, host)` | `LdapConnection(new LdapDirectoryIdentifier(host, port))` + `SessionOptions.SecureSocketLayer = true` (LDAPS) + `AuthType.Basic` bind using service account DN + password |
| `UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, user)` | `SearchRequest(baseDN, "(&(objectClass=user)(samAccountName={0}))", SearchScope.Subtree)` — parameterized filter |
| `AllowedUsernameAttributes` fallback (`samaccountname` → `userprincipalname` → `mail`) | Same fallback order, different LDAP filters per attribute |
| `user.ChangePassword(old, new)` | Two `DirectoryAttributeModification` operations in one `ModifyRequest`: `Delete unicodePwd="\"old\""` + `Add unicodePwd="\"new\""`. UTF-16LE encoded, quoted per AD's documented atomic change-password pattern. |
| `AllowSetPasswordFallback` → `user.SetPassword(new)` | `Replace unicodePwd="\"new\""` ModifyRequest. Opt-in, default off. |
| `user.IsAccountLockedOut()` | Decode `msDS-User-Account-Control-Computed` (preferred) or `userAccountControl` flags via `SearchRequest` |
| `user.UserCannotChangePassword` | **Deferred.** Logged warning at startup on Linux. AD's server-side modify rejection provides enforcement without the ACE check. |
| `user.GetAuthorizationGroups()` for group membership | `tokenGroups` recursive walk via `SearchRequest` on the user DN with attribute list `["tokenGroups"]` |
| `user.LastPasswordSet` | Read `pwdLastSet` attribute (Windows filetime `long`) |
| Domain `minPwdAge` from `Domain.GetCurrentDomain()` | Read `minPwdAge` attribute on the domain root DSE (Windows filetime `long`, negative) |
| `COMException` HRESULT-based error mapping | `DirectoryOperationException.Response.ResultCode` + `extendedError` DWORD mapped to the same `ApiErrorCode`s. Mapping table lives in `LdapErrorMapping.cs`. |

### New configuration schema

Added to `PasswordChangeOptions`:

```json
"PasswordChangeOptions": {
  "ProviderMode": "Auto",              // enum: Auto | Windows | Ldap (default: Auto)
  "LdapHostname": "dc01.corp.example.com",
  "LdapPort": 636,
  "ServiceAccountDn": "CN=svc-passreset,OU=ServiceAccounts,DC=corp,DC=example,DC=com",
  "ServiceAccountPassword": "${PASSRESET_SERVICEACCOUNTPASSWORD}",
  "BaseDn": "DC=corp,DC=example,DC=com",
  "LdapTrustedCertificateThumbprints": [],
  // existing keys preserved
  "AllowedUsernameAttributes": ["samaccountname", "userprincipalname", "mail"],
  "AllowSetPasswordFallback": false,
  "PortalLockoutThreshold": 3,
  "PortalLockoutWindow": "00:30:00"
}
```

- `ServiceAccountPassword` follows the STAB-017 env-var binding pattern — never plaintext in committed config.
- `LdapTrustedCertificateThumbprints` mirrors the existing `SmtpSettings.TrustedCertificateThumbprints` pattern for internal-CA trust on Linux.
- `UseAutomaticContext` (current Windows provider setting) remains valid for `ProviderMode=Windows` but is ignored by the Ldap provider.

Schema validation (`IValidateOptions<PasswordChangeOptions>`):
1. `ProviderMode=Ldap` requires `LdapHostname`, `ServiceAccountDn`, `ServiceAccountPassword`, `BaseDn` all non-empty.
2. `ProviderMode=Windows` on non-Windows platform → `OptionsValidationException` at `ValidateOnStart()`.
3. `ProviderMode=Auto` on non-Windows requires the Ldap fields (Auto resolves to Ldap there).

Upgrade path: Phase 08 config-schema-sync automatically adds `"ProviderMode": "Auto"` to existing v1.4.x `appsettings.Production.json` files on upgrade. Windows operators see no behavior change (`Auto` picks Windows). Linux installs ship with `"ProviderMode": "Ldap"` pre-set in the template.

### Health endpoint (STAB-018 reuse)

`/api/health` today probes AD connectivity via `TcpClient.ConnectAsync(host, port)` with a 3-second CTS. Provider-agnostic by luck — both providers use the same probe unchanged. No Phase 11 work on the health endpoint itself.

### `ILdapSession` thin adapter

The new provider depends on an `ILdapSession` interface rather than `LdapConnection` directly, for testability:

```csharp
public interface ILdapSession : IDisposable
{
    void Bind();
    SearchResponse Search(SearchRequest request);
    ModifyResponse Modify(ModifyRequest request);
    // ~5 methods total
}
```

A default `LdapSession` class wraps `LdapConnection`. Tests inject a fake. Lives in `PassReset.PasswordProvider.Ldap`; no separate abstractions project unless tests prove it awkward.

## Testing strategy

Three layers:

### Layer 1 — Unit tests (`PassReset.Tests`, cross-platform)

- `LdapPasswordChangeProviderTests` mirrors `PasswordChangeProviderTests` case-by-case.
- `FakeLdapSession` implements `ILdapSession` and simulates AD quirks that matter for error mapping: `ResultCode.InsufficientAccessRights`, `ResultCode.ConstraintViolation` with `extendedError = 0x0000052D` (password policy violation), `extendedError = 0x00000775` (account locked), etc.
- Runs on Windows + Linux CI. No special infrastructure.

### Layer 2 — Contract tests (new, shared across providers)

- `IPasswordChangeProviderContract` — abstract xUnit test class defining ~12 behavioral scenarios:
  - `InvalidCredentials returns correct code`
  - `WrongPassword increments lockout counter`
  - `DisallowedGroup returns ChangeNotPermitted`
  - `MinPwdAge violation returns PasswordTooRecentlyChanged` (STAB-004)
  - `PasswordPolicyViolation returns correct code`
  - `UserNotFound returns correct code after all attribute fallbacks`
  - … (full list finalized during planning)
- `PasswordChangeProviderContractTests : IPasswordChangeProviderContract` (Windows, uses AccountManagement fakes)
- `LdapPasswordChangeProviderContractTests : IPasswordChangeProviderContract` (Ldap, uses `FakeLdapSession`)
- **Core parity proof.** If a contract test passes on one provider and fails on the other, that's a migration bug.

### Layer 3 — Integration tests (new CI infrastructure)

- GitHub Actions service container: `samba-ad-dc` Docker image preconfigured as AD-compatible LDAP directory.
- End-to-end flow: seed test user, service-account bind, change password via real `LdapConnection`, re-bind with new password, assert success.
- Covers LDAPS cert trust, bind auth, actual `unicodePwd` UTF-16LE quirks, ResultCode shape from a real directory.
- New CI job, parallel to `tests`. Adds ~3-5 min. Runs on Linux only (Samba image is Linux-native).
- Phase 11 ships with this job in **warning-only** mode (non-blocking). Promotion to required status check is a follow-up after stability is proven.

### Coverage targets

- Ldap provider line coverage: ≥ 70% (matches current Windows provider)
- Branch coverage: ≥ 55%
- 100% of `ApiErrorCode` values mapped by `LdapErrorMapping.cs` must be exercised by at least one contract or unit test.

## Risks & mitigations

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| AD rejects `unicodePwd` modify because the service account lacks "Reset Password" / "Change Password" extended right on the target OU | High (misconfigured domains) | Clear error at first password change attempt. New doc `docs/AD-ServiceAccount-LDAP-Setup.md` with the exact AD Users & Computers / PowerShell steps to grant the right. |
| LDAPS certificate trust differs on Linux (Windows auto-trusts domain CA; Linux needs explicit trust) | High | `LdapConnection.SessionOptions.VerifyServerCertificate` callback. Config option `LdapTrustedCertificateThumbprints` (mirrors existing `SmtpSettings.TrustedCertificateThumbprints`). Docs cover installing the root cert into the Linux cert store as an alternative. |
| Samba DC container behaviors diverge from real AD on `unicodePwd` / `minPwdAge` details | Medium | Unit + contract tests are the real correctness gate. Samba is a smoke test for end-to-end wiring, not a semantic authority. Document known divergences and move on. |
| `AllowSetPasswordFallback` (STAB-004 adjacent) triggers on different LDAP error codes than the current `COMException` HRESULT trap | Medium | Decide during implementation by observing real AD error codes in the Samba container and (if possible) a test real-AD lab. Fallback is opt-in, defaults off — slightly different trigger condition is acceptable. |
| `tokenGroups` walk semantics differ on forests with multiple domains | Low | Already an implicit PassReset constraint (single-domain). Make it explicit in docs. |

## Open questions (resolve during `/gsd-plan-phase 11`)

1. Where does `ILdapSession` live — inside `PassReset.PasswordProvider.Ldap` or in a separate `PassReset.PasswordProvider.Ldap.Abstractions` project shared with tests? Default: former, unless tests prove it awkward.
2. How to inject the Samba integration-test service account password without committing secrets — `appsettings.IntegrationTest.json` gitignored + GitHub Actions secret. Plan confirms the exact env-var name.
3. `IPasswordChangeProvider` interface leak audit: verify today's interface never exposes Windows-specific types in signatures. Treat any leak as a Phase 11 bug to fix.

## Success criteria

1. `LdapPasswordChangeProvider` passes every contract test that `PasswordChangeProvider` passes on Windows.
2. Samba DC integration test completes a full end-to-end change-password flow on Linux CI.
3. `PassReset.Web` compiles and full test suite passes on **both** Windows and Linux (Windows default → Windows provider; Linux → Ldap provider).
4. Existing Windows production deployments upgrade to v2.0-alpha1 with no `appsettings.Production.json` changes required and no observable behavior differences.
5. New operator doc `docs/AD-ServiceAccount-LDAP-Setup.md` published covering: service account creation, "Change Password" extended right grant, LDAPS cert trust for Linux, troubleshooting matrix mapping common `ResultCode`s to fixes.
6. CHANGELOG entry under `[2.0.0-alpha.1]` describes the new `ProviderMode` setting and links to the setup doc.

## Artifacts produced by this phase

- `src/PassReset.PasswordProvider.Ldap/LdapPasswordChangeProvider.cs` — new provider
- `src/PassReset.PasswordProvider.Ldap/ILdapSession.cs` + `LdapSession.cs` — adapter
- `src/PassReset.PasswordProvider.Ldap/LdapErrorMapping.cs` — ResultCode → ApiErrorCode
- `src/PassReset.PasswordProvider.Ldap/PassReset.PasswordProvider.Ldap.csproj`
- `src/PassReset.Web/PassReset.Web.csproj` — retargeted to `net10.0`, conditional Windows provider ref
- `src/PassReset.Web/Program.cs` — provider selection block, new `ProviderMode` resolution
- `src/PassReset.Web/Models/PasswordChangeOptions.cs` — new fields
- `src/PassReset.Web/appsettings.schema.json` — schema updates
- `src/PassReset.Web/appsettings.Production.template.json` — new fields with placeholders
- `src/PassReset.Tests/PassReset.Tests.csproj` — retargeted to `net10.0`
- `src/PassReset.Tests/Contracts/IPasswordChangeProviderContract.cs` — new abstract contract
- `src/PassReset.Tests/Contracts/LdapPasswordChangeProviderContractTests.cs` — Ldap impl
- `src/PassReset.Tests/Services/LdapPasswordChangeProviderTests.cs` — Ldap unit tests
- `src/PassReset.Tests.Windows/PassReset.Tests.Windows.csproj` — NEW Windows-specific test project
- `src/PassReset.Tests.Windows/Contracts/PasswordChangeProviderContractTests.cs` — Windows impl of the contract
- `.github/workflows/tests.yml` — new `integration-tests` job (warning-only) with Samba DC service container
- `docs/AD-ServiceAccount-LDAP-Setup.md` — new operator doc
- `docs/appsettings-Production.md` — updated with new fields
- `CHANGELOG.md` — `[2.0.0-alpha.1]` section

## Dependencies & sequencing

- **Depends on:** v1.4.2 (shipped). All Phase 11 work starts from `master` as-is.
- **Blocks:** Phase 12 (Kestrel-direct hosting) — which assumes the provider works on Linux.
- **Parallel-safe with:** Nothing in v2.0 so far. Phase 11 is the critical path.

---

**End of spec.**
