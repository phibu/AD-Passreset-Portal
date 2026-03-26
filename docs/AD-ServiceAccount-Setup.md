# ReKey — Active Directory Service Account Setup

This guide covers every permission ReKey needs in Active Directory, three identity options (standard account, gMSA, domain-joined auto-context), and how to verify the configuration end-to-end.

---

## Overview — Which identity model should I use?

| Scenario | Identity | `UseAutomaticContext` | Stored password? |
|---|---|---|---|
| Domain-joined IIS server + gMSA | `DOMAIN\svc-rekey$` | `true` | No — AD manages it |
| Domain-joined IIS server + service account | `DOMAIN\svc-rekey` | `true` | Yes — in `appsettings.Production.json` |
| Non-domain-joined IIS server | `DOMAIN\svc-rekey` | `false` | Yes — in `appsettings.Production.json` |
| Development / debug mode | N/A (`UseDebugProvider: true`) | N/A | No |

**Recommendation:** domain-joined server + gMSA — simplest and most secure.

---

## Option A — Standard service account (username + password)

### Step 1 — Create the account

In **Active Directory Users and Computers** or via PowerShell on a domain controller:

```powershell
New-ADUser `
    -Name              "ReKey Service" `
    -SamAccountName    "svc-rekey" `
    -UserPrincipalName "svc-rekey@yourdomain.com" `
    -Path              "OU=Service Accounts,DC=yourdomain,DC=com" `
    -AccountPassword   (Read-Host -AsSecureString "Enter password") `
    -PasswordNeverExpires $true `
    -CannotChangePassword $true `
    -Enabled           $true
```

Record the password — it goes into `appsettings.Production.json` (Step 5).

---

### Step 2 — Delegate "Reset Password" on the user OU

ReKey must be able to reset passwords for users. Delegate this right on the OU (or OUs) that contain your user accounts.

#### Via the ADUC delegation wizard

1. Open **ADUC** → expand the domain → right-click the target OU → **Delegate Control…**
2. Click **Add…** → type `svc-rekey` → **OK**
3. Select **Create a custom task to delegate** → **Next**
4. Select **Only the following objects in the folder** → tick **User objects** → **Next**
5. Under **Permissions**, tick:
   - ✓ **Reset Password**
   - ✓ **Read and write `pwdLastSet`** *(required for "must change at next logon" flag)*
   - ✓ **Read and write `lockoutTime`** *(optional — for a future account-unlock feature)*
6. Click **Next** → **Finish**

#### Via PowerShell (`dsacls`)

```powershell
$ou  = "OU=Users,DC=yourdomain,DC=com"
$sid = (Get-ADUser svc-rekey).SID.Value

# Reset password
dsacls $ou /G "${sid}:CA;Reset Password;User"

# Read/write pwdLastSet
dsacls $ou /G "${sid}:RPWP;pwdLastSet;User"

# Read/write lockoutTime (optional)
dsacls $ou /G "${sid}:RPWP;lockoutTime;User"
```

If your users span multiple OUs, run the commands once per OU.

---

### Step 3 — Verify read access to user attributes

ReKey reads the following attributes when looking up a user account:

| Attribute | Purpose |
|---|---|
| `sAMAccountName` | User lookup |
| `userPrincipalName` | User lookup |
| `distinguishedName` | User lookup |
| `mail` | Password-changed email notification |
| `memberOf` | Allow/block group enforcement |
| `pwdLastSet` | Minimum password age and expiry reminders |
| `userAccountControl` | Account state (disabled, locked, must-change) |
| `maxPwdAge` / `minPwdAge` | Domain password policy |
| `minPwdLength` | Domain minimum password length |

These attributes are readable by all authenticated users in a default AD configuration. Verify with:

```powershell
Get-ADUser <test-username> `
    -Properties mail, memberOf, pwdLastSet, userAccountControl `
    -Credential (Get-Credential YOURDOMAIN\svc-rekey) |
    Select-Object SamAccountName, Mail, PwdLastSet, UserAccountControl
```

If the call succeeds and returns values, no extra delegation is needed.

If your domain has restricted default read permissions (common in high-security environments), grant **Read** on the attributes above for the target OU:

```powershell
$ou  = "OU=Users,DC=yourdomain,DC=com"
$sid = (Get-ADUser svc-rekey).SID.Value

foreach ($attr in @("mail","memberOf","pwdLastSet","userAccountControl","distinguishedName")) {
    dsacls $ou /G "${sid}:RP;${attr};User"
}
```

---

### Step 4 — Verify domain password policy read access

ReKey reads domain-level password policy to enforce minimum age and display length requirements. The account needs read access to the domain root object:

```powershell
# Should return the domain's password policy values
Get-ADDefaultDomainPasswordPolicy -Credential (Get-Credential YOURDOMAIN\svc-rekey)
```

This succeeds by default for all authenticated users. No delegation required unless domain object permissions have been hardened.

---

### Step 5 — Configure `appsettings.Production.json`

```json
{
  "PasswordChangeOptions": {
    "UseAutomaticContext": false,
    "LdapHostnames":  [ "dc01.yourdomain.com", "dc02.yourdomain.com" ],
    "LdapPort":       389,
    "LdapUsername":   "YOURDOMAIN\\svc-rekey",
    "LdapPassword":   "<password from Step 1>",
    "DefaultDomain":  "yourdomain.com"
  }
}
```

> **LDAPS (recommended for non-domain-joined servers)**
> Change `LdapPort` to `636`. Ensure the DC's certificate is trusted by the IIS server (import the CA root into `Cert:\LocalMachine\Root`).

---

## Option B — Group Managed Service Account (gMSA) — Recommended

gMSAs have their passwords managed automatically by Active Directory — no stored credentials anywhere. Requires Windows Server 2012 domain functional level or higher.

### Step 1 — Ensure the KDS Root Key exists

```powershell
# Run on a domain controller — check if the key exists
Get-KdsRootKey

# If no output, create it (takes up to 10 hours to replicate; use -EffectiveImmediately in lab)
Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10))
```

### Step 2 — Create an AD group for the IIS servers

```powershell
New-ADGroup `
    -Name        "ReKey-Servers" `
    -GroupScope  Global `
    -Path        "OU=Groups,DC=yourdomain,DC=com"

# Add the IIS server computer account to the group
Add-ADGroupMember -Identity "ReKey-Servers" -Members "IISSERVER$"
```

> After adding the server to the group, reboot it (or run `klist purge` + `gpupdate /force`) so it picks up the new group membership.

### Step 3 — Create the gMSA

```powershell
New-ADServiceAccount `
    -Name                                         "svc-rekey" `
    -DNSHostName                                  "rekey.yourdomain.com" `
    -PrincipalsAllowedToRetrieveManagedPassword   "ReKey-Servers"
```

### Step 4 — Install and test on the IIS server

```powershell
# On the IIS server (requires RSAT AD PowerShell module)
Install-ADServiceAccount svc-rekey

# Verify — must return True
Test-ADServiceAccount svc-rekey
```

### Step 5 — Delegate permissions (same as Option A Steps 2–4)

The gMSA needs the same AD permissions as a standard service account. Run the `dsacls` commands from Option A Steps 2 and 3, using the gMSA's SID:

```powershell
$ou  = "OU=Users,DC=yourdomain,DC=com"
$sid = (Get-ADServiceAccount "svc-rekey").SID.Value

dsacls $ou /G "${sid}:CA;Reset Password;User"
dsacls $ou /G "${sid}:RPWP;pwdLastSet;User"
dsacls $ou /G "${sid}:RPWP;lockoutTime;User"
```

### Step 6 — Set the IIS app pool identity

In **IIS Manager** → **Application Pools** → **ReKeyPool** → **Advanced Settings** → **Identity** → **Custom account**:

- **Username:** `YOURDOMAIN\svc-rekey$` *(trailing dollar sign is required)*
- **Password:** *(leave blank)*

Or via PowerShell:

```powershell
Import-Module WebAdministration

Set-ItemProperty "IIS:\AppPools\ReKeyPool" -Name processModel -Value @{
    userName     = "YOURDOMAIN\svc-rekey$"
    password     = ""
    identityType = "SpecificUser"
}
```

### Step 7 — Configure `appsettings.Production.json`

With a gMSA on a domain-joined server, `UseAutomaticContext: true` — no LDAP credentials needed:

```json
{
  "PasswordChangeOptions": {
    "UseAutomaticContext": true,
    "DefaultDomain": "yourdomain.com"
  }
}
```

---

## Option C — Domain-joined server with named service account (automatic context)

If the IIS server is domain-joined and the app pool runs as a named domain account (not gMSA), set `UseAutomaticContext: true` and omit the LDAP credential settings. ReKey authenticates using the process token of the app pool identity.

Follow Option A Steps 1–4 to create the account and delegate permissions. Set the IIS app pool identity to `YOURDOMAIN\svc-rekey` with its password, then in `appsettings.Production.json`:

```json
{
  "PasswordChangeOptions": {
    "UseAutomaticContext": true,
    "DefaultDomain": "yourdomain.com"
  }
}
```

---

## Permission summary

| Permission | Required | Purpose |
|---|---|---|
| Reset Password on user OU | ✅ Yes | Core password change operation |
| Read/write `pwdLastSet` on user OU | ✅ Yes | Clear "must change at next logon"; enforce minimum age |
| Read user attributes (`mail`, `memberOf`, etc.) | ✅ Yes (default) | Group enforcement, email notifications |
| Read domain password policy | ✅ Yes (default) | Minimum age and length enforcement |
| Read/write `lockoutTime` on user OU | ⬜ Optional | Future account unlock feature |

---

## Verification checklist

Run these from the IIS server after completing the setup:

```powershell
# 1. Confirm the gMSA is installed (gMSA only)
Test-ADServiceAccount svc-rekey          # Must return True

# 2. Confirm password reset delegation
$cred = Get-Credential YOURDOMAIN\svc-rekey
Set-ADAccountPassword <test-user> `
    -NewPassword (Read-Host -AsSecureString "New PW") `
    -Credential $cred

# 3. Confirm attribute read access
Get-ADUser <test-user> -Properties mail, memberOf, pwdLastSet -Credential $cred

# 4. Confirm domain policy read
Get-ADDefaultDomainPasswordPolicy -Credential $cred

# 5. End-to-end: use ReKey with a real user account
# Set "UseDebugProvider": false in appsettings.Production.json
# Attempt a password change via the UI
```

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| "Invalid credentials" on every attempt | Wrong `LdapUsername` / `LdapPassword` in config | Test with `Get-ADUser` using explicit credential |
| "Access denied" on password reset | Reset Password not delegated on the OU | Re-run `dsacls` delegation (Step 2) |
| `pwdLastSet` not updating | `pwdLastSet` write not delegated | Add `RPWP;pwdLastSet` delegation |
| `Test-ADServiceAccount` returns `False` | gMSA not installed, or server not in the allowed principals group | Reboot server after adding to `ReKey-Servers`; re-run `Install-ADServiceAccount` |
| Email notifications not sent | `mail` attribute empty on the user object | Populate `mail` in ADUC or via `Set-ADUser -EmailAddress` |
| Group allow/block list not working | `memberOf` read denied | Add read delegation on `memberOf` for the target OU |
| LDAPS connection refused | DC certificate not trusted by IIS server | Import the CA root cert into `Cert:\LocalMachine\Root` on the IIS server |

---

*For IIS installation and certificate setup, see [`IIS-Setup.md`](IIS-Setup.md).*
*For deploy scripts and publish workflow, see [`../deploy/`](../deploy/).*
