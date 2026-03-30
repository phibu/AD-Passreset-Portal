# appsettings.Production.json Reference

This file overrides `appsettings.json` in production. Place it in the same folder as `PassReset.Web.exe` (e.g. `C:\inetpub\PassReset\`). The install script creates a starter copy automatically — edit it before starting the site.

---

## WebSettings

```json
"WebSettings": {
  "EnableHttpsRedirect": true,
  "UseDebugProvider": false
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `EnableHttpsRedirect` | bool | `true` | Redirects HTTP requests to HTTPS. |
| `UseDebugProvider` | bool | `false` | Bypasses AD authentication — accepts any password. **Never enable in production.** |

---

## PasswordChangeOptions

```json
"PasswordChangeOptions": {
  "UseAutomaticContext": true,
  "AllowedUsernameAttributes": [ "samaccountname" ],
  "IdTypeForUser": "UserPrincipalName",
  "PortalLockoutThreshold": 3,
  "PortalLockoutWindow": "00:30:00",
  "DefaultDomain": "yourdomain.com",
  "ClearMustChangePasswordFlag": true,
  "EnforceMinimumPasswordAge": true,
  "FailOpenOnPwnedCheckUnavailable": false,
  "AllowSetPasswordFallback": false,
  "UpdateLastPassword": false,
  "RestrictedAdGroups": [ "Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators" ],
  "AllowedAdGroups": [],
  "LdapHostnames": [ "dc01.yourdomain.com" ],
  "LdapPort": 636,
  "LdapUseSsl": true,
  "LdapUsername": "",
  "LdapPassword": ""
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `UseAutomaticContext` | bool | `true` | `true` = domain-joined server, uses machine credentials automatically. `false` = supply `LdapHostnames`, `LdapUsername`, `LdapPassword`. |
| `AllowedUsernameAttributes` | string[] | `["samaccountname"]` | AD attributes tried in order when looking up a user. Options: `samaccountname`, `userprincipalname`, `mail`. |
| `IdTypeForUser` | string | `UserPrincipalName` | How the user identity is bound after lookup. Options: `UserPrincipalName`, `SamAccountName`, `DistinguishedName`, `Sid`, `Guid`, `Name`. |
| `PortalLockoutThreshold` | int | `3` | Number of consecutive wrong-password attempts before the portal blocks further attempts (without touching AD). `0` = disabled. |
| `PortalLockoutWindow` | string | `"00:30:00"` | Duration of the portal lockout window (`hh:mm:ss`). The window is absolute — it starts at the first failure and is not reset by subsequent attempts. |
| `DefaultDomain` | string | `""` | Appended to bare usernames (e.g. `jsmith` → `jsmith@yourdomain.com`) when `IdTypeForUser` is `UserPrincipalName`. |
| `ClearMustChangePasswordFlag` | bool | `true` | Clears the "must change password at next logon" AD flag after a successful change. |
| `EnforceMinimumPasswordAge` | bool | `true` | Blocks changes before the AD minimum password age (minPwdAge) has elapsed. |
| `UpdateLastPassword` | bool | `false` | Updates the `pwdLastSet` attribute after change. Usually not required. |
| `RestrictedAdGroups` | string[] | See default | Users in these groups are blocked from changing their password. |
| `AllowedAdGroups` | string[] | `[]` | If non-empty, only users in these groups may use the tool. Leave empty to allow all users. |
| `LdapHostnames` | string[] | `[""]` | One or more hostnames or IPs of domain controllers. Used when `UseAutomaticContext` is `false`. |
| `LdapPort` | int | `636` | LDAP/LDAPS port. Default `636` (LDAPS). Use `389` for plain LDAP (not recommended). |
| `LdapUseSsl` | bool | `true` | Enables LDAPS (LDAP over TLS). Set to `false` only when LDAPS is unavailable. |
| `LdapUsername` | string | `""` | Service account UPN or SAM for LDAP bind. Used when `UseAutomaticContext` is `false`. |
| `FailOpenOnPwnedCheckUnavailable` | bool | `false` | When `true`, allows password changes to proceed when the HIBP API is unreachable (breach check is skipped). When `false`, an unreachable API blocks the change. |
| `AllowSetPasswordFallback` | bool | `false` | When `true`, falls back to the administrative `SetPassword` API on COMException. **Warning:** this may bypass AD password history enforcement. |
| `LdapPassword` | string | `""` | Password for `LdapUsername`. Store securely — consider using environment variable substitution or a secrets manager. |
| `NotificationEmailStrategy` | string | `"Mail"` | How the recipient email address is resolved for password-changed notifications. See table below. |
| `NotificationEmailDomain` | string | `""` | Domain suffix used with `SamAccountNameAtDomain` strategy. Falls back to `DefaultDomain` when empty. |
| `NotificationEmailTemplate` | string | `""` | Template string used with `Custom` strategy. Placeholders: `{samaccountname}`, `{userprincipalname}`, `{mail}`, `{defaultdomain}`. Example: `{samaccountname}@{defaultdomain}` |

### NotificationEmailStrategy values

| Value | Address resolved as | Example |
|-------|---------------------|---------|
| `Mail` | AD `mail` attribute (default) | `jane.doe@company.com` |
| `UserPrincipalName` | AD `userPrincipalName` attribute | `jdoe@company.com` |
| `SamAccountNameAtDomain` | `{samaccountname}@{NotificationEmailDomain}` | `jdoe@company.com` |
| `Custom` | Evaluate `NotificationEmailTemplate` | `{samaccountname}@{defaultdomain}` |

---

## SmtpSettings

```json
"SmtpSettings": {
  "Host": "smtp-relay.yourdomain.com",
  "Port": 587,
  "UseSsl": true,
  "Username": "",
  "Password": "",
  "FromAddress": "passreset@yourdomain.com",
  "FromName": "PassReset Self-Service"
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Host` | string | SMTP relay hostname. Leave empty to disable all outbound email. |
| `Port` | int | `587` = STARTTLS (recommended). `465` = SMTPS. `25` = unauthenticated relay. |
| `UseSsl` | bool | Enables TLS. Set `false` only for unauthenticated internal relays on port 25. |
| `Username` | string | SMTP authentication username. Leave empty for anonymous relay. |
| `Password` | string | SMTP authentication password. |
| `FromAddress` | string | Sender email address shown in notifications. |
| `FromName` | string | Sender display name shown in notifications. |

---

## EmailNotificationSettings

Sends a confirmation email to the user after a successful password change.

```json
"EmailNotificationSettings": {
  "Enabled": false,
  "Subject": "Your password has been changed",
  "BodyTemplate": "Hello {Username},\n\nYour password was changed successfully on {Timestamp} from IP address {IpAddress}.\n\nIf you did not make this change, contact IT Support immediately."
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable change notifications. Requires `SmtpSettings.Host` to be set. |
| `Subject` | string | Email subject line. |
| `BodyTemplate` | string | Email body. Supports `{Username}`, `{Timestamp}`, `{IpAddress}` placeholders. |

---

## PasswordExpiryNotificationSettings

A background service that emails users before their password expires. Scans members of `AllowedAdGroups` daily.

```json
"PasswordExpiryNotificationSettings": {
  "Enabled": false,
  "DaysBeforeExpiry": 14,
  "NotificationTimeUtc": "08:00",
  "PassResetUrl": "https://passreset.yourdomain.com",
  "ExpiryEmailSubject": "Your password will expire soon",
  "ExpiryEmailBodyTemplate": "Hello {Username},\n\nYour Active Directory password will expire in {DaysRemaining} day(s) on {ExpiryDate}.\n\nPlease change your password before it expires: {PassResetUrl}"
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable the expiry reminder service. |
| `DaysBeforeExpiry` | int | How many days before expiry to start sending reminders. |
| `NotificationTimeUtc` | string | Time of day (UTC, `HH:mm`) the daily scan runs. |
| `PassResetUrl` | string | URL of this tool, embedded in reminder emails. |
| `ExpiryEmailSubject` | string | Email subject. |
| `ExpiryEmailBodyTemplate` | string | Supports `{Username}`, `{DaysRemaining}`, `{ExpiryDate}`, `{PassResetUrl}`. |

---

## ClientSettings

Controls the UI and frontend behaviour.

```json
"ClientSettings": {
  "ApplicationTitle": "Change Account Password | Self-Service",
  "ChangePasswordTitle": "Change Account Password",
  "UseEmail": false,
  "ShowPasswordMeter": true,
  "UsePasswordGeneration": false,
  "MinimumDistance": 0,
  "PasswordEntropy": 16,
  "MinimumScore": 0,
  "AllowedUsernameAttributes": [ "samaccountname" ],
  "Recaptcha": {
    "Enabled": false,
    "SiteKey": "",
    "PrivateKey": "",
    "LanguageCode": "en"
  },
  "ValidationRegex": {
    "EmailRegex": "^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9-]+(?:\\.[a-zA-Z0-9-]+)*$",
    "UsernameRegex": ""
  },
  "ChangePasswordForm": {
    "HelpText": "If you are having trouble with this tool, please contact IT Support.",
    "UsernameLabel": "Username",
    "UsernameHelpblock": "Your organisation email address",
    "UsernameDefaultDomainHelperBlock": "Your organisation username",
    "CurrentPasswordLabel": "Current Password",
    "CurrentPasswordHelpblock": "",
    "NewPasswordLabel": "New Password",
    "NewPasswordHelpblock": "Choose a strong password.",
    "NewPasswordVerifyLabel": "Confirm New Password",
    "NewPasswordVerifyHelpblock": "",
    "ChangePasswordButtonLabel": "Change Password"
  },
  "ErrorsPasswordForm": {
    "FieldRequired": "This field is required.",
    "PasswordMatch": "Passwords do not match.",
    "UsernameEmailPattern": "Please enter a valid email address.",
    "UsernamePattern": "Please enter a valid username."
  },
  "Alerts": {
    "SuccessAlertTitle": "Password changed successfully.",
    "SuccessAlertBody": "Please note it may take a few minutes for your new password to reach all domain controllers.",
    "ErrorPasswordChangeNotAllowed": "You are not allowed to change your password. Please contact IT Support.",
    "ErrorInvalidCredentials": "Your current password is incorrect.",
    "ErrorInvalidDomain": "Invalid domain. Please check your username and try again.",
    "ErrorInvalidUser": "User account not found.",
    "ErrorCaptcha": "Could not verify you are not a robot. Please try again.",
    "ErrorFieldRequired": "Please fill in all required fields.",
    "ErrorFieldMismatch": "The new passwords do not match.",
    "ErrorComplexPassword": "The new password does not meet complexity requirements.",
    "ErrorConnectionLdap": "Could not connect to the directory. Please contact IT Support.",
    "ErrorScorePassword": "The password is not strong enough. Please choose a stronger password.",
    "ErrorDistancePassword": "The new password is too similar to your current password.",
    "ErrorPwnedPassword": "This password has been found in public breach databases. Please choose a different password.",
    "ErrorPasswordTooYoung": "Your password was changed too recently. Please wait before changing it again.",
    "ErrorRateLimitExceeded": "Too many attempts. Please wait a few minutes and try again.",
    "ErrorPwnedPasswordCheckFailed": "The password breach check service is temporarily unavailable. Please try again in a moment.",
    "ErrorPortalLockout": "Too many failed attempts. Please wait 30 minutes before trying again.",
    "ErrorApproachingLockout": "Incorrect password. Warning: one more failed attempt will temporarily lock your access to this portal."
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `ApplicationTitle` | string | Browser tab title. |
| `ChangePasswordTitle` | string | Heading shown on the form card. |
| `UseEmail` | bool | If `true`, the username field accepts an email address (legacy; prefer `AllowedUsernameAttributes`). |
| `ShowPasswordMeter` | bool | Displays a password strength indicator (zxcvbn) in the form. |
| `UsePasswordGeneration` | bool | Adds a "generate password" button to the new-password field. |
| `MinimumDistance` | int | Minimum Levenshtein distance between old and new password. `0` = disabled. Enforced client- and server-side. |
| `PasswordEntropy` | int | Entropy bits used by the password generator. |
| `MinimumScore` | int | Minimum zxcvbn score (0–4). `0` = disabled. UI feedback only — not enforced server-side. |
| `AllowedUsernameAttributes` | string[] | AD attributes the username field accepts. Options: `samaccountname`, `userprincipalname`, `mail`. Controls the helper text shown below the username field. Must match `PasswordChangeOptions.AllowedUsernameAttributes`. |

### Recaptcha

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable Google reCAPTCHA v3. |
| `SiteKey` | string | reCAPTCHA v3 site key (public). Loaded in the browser. |
| `PrivateKey` | string | reCAPTCHA v3 secret key. Used server-side only — never exposed to the client. |
| `LanguageCode` | string | reCAPTCHA widget language (e.g. `en`, `de`, `fr`). |

### ValidationRegex

| Key | Type | Description |
|-----|------|-------------|
| `EmailRegex` | string | Regex applied to the username field when `AllowedUsernameAttributes` contains only email-format attributes (`userprincipalname`, `mail`). |
| `UsernameRegex` | string | Regex applied to the username field when `samaccountname` is the sole allowed attribute. Leave empty to skip pattern validation. |

### ChangePasswordForm

All strings are optional — defaults are built into the React app. Override any key to localise or customise the form.

| Key | Description |
|-----|-------------|
| `HelpText` | Paragraph shown above the form. |
| `UsernameLabel` | Label for the username field. |
| `UsernameHelpblock` | Helper text shown below the username field when `UseEmail` is `true`. |
| `UsernameDefaultDomainHelperBlock` | Helper text shown when `samaccountname` is the accepted attribute. |
| `CurrentPasswordLabel` | Label for the current-password field. |
| `NewPasswordLabel` | Label for the new-password field. |
| `NewPasswordHelpblock` | Helper text below the new-password field. |
| `NewPasswordVerifyLabel` | Label for the confirm-password field. |
| `ChangePasswordButtonLabel` | Submit button text. |

### ErrorsPasswordForm

Client-side validation messages (shown before the form is submitted).

| Key | Description |
|-----|-------------|
| `FieldRequired` | Shown when a required field is empty. |
| `PasswordMatch` | Shown when new password and confirmation do not match. |
| `UsernameEmailPattern` | Shown when the username fails the email regex. |
| `UsernamePattern` | Shown when the username fails the username regex. |

### Alerts

Server error and success messages returned from the API. All keys are optional; built-in defaults are shown in the JSON example above.

| Key | Description |
|-----|-------------|
| `SuccessAlertTitle` | Heading on the success card. |
| `SuccessAlertBody` | Body text on the success card. |
| `ErrorInvalidCredentials` | Wrong current password. |
| `ErrorInvalidUser` | Username not found in AD. |
| `ErrorPasswordChangeNotAllowed` | User is in a restricted group. |
| `ErrorInvalidDomain` | Domain portion of the username is not recognised. |
| `ErrorCaptcha` | reCAPTCHA verification failed. |
| `ErrorComplexPassword` | New password does not meet AD complexity rules. |
| `ErrorConnectionLdap` | Could not reach a domain controller. |
| `ErrorScorePassword` | Password zxcvbn score is below `MinimumScore`. |
| `ErrorDistancePassword` | New password is too similar to the current one. |
| `ErrorPwnedPassword` | Password found in HIBP breach database. |
| `ErrorPasswordTooYoung` | AD minimum password age has not elapsed. |
| `ErrorRateLimitExceeded` | Built-in rate limiter (5 req / 5 min) triggered. |
| `ErrorPwnedPasswordCheckFailed` | HIBP API was unreachable; change was blocked. |
| `ErrorPortalLockout` | Portal lockout threshold reached; AD not contacted. |
| `ErrorApproachingLockout` | Wrong password and one more attempt will trigger portal lockout. |

---

## SiemSettings

Forwards security events to a SIEM via RFC 5424 syslog and/or email alerts. Both channels are opt-in; all keys are optional.

```json
"SiemSettings": {
  "Syslog": {
    "Enabled": false,
    "Host": "siem.yourdomain.com",
    "Port": 514,
    "Protocol": "UDP",
    "Facility": 10,
    "AppName": "PassReset"
  },
  "AlertEmail": {
    "Enabled": false,
    "Recipients": [ "security@yourdomain.com" ],
    "AlertOnEvents": [ "PortalLockout", "InvalidCredentials" ]
  }
}
```

### Syslog

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Set to `true` to enable syslog forwarding. |
| `Host` | string | `""` | Hostname or IP of the syslog collector / SIEM. |
| `Port` | int | `514` | UDP/TCP port of the syslog collector. |
| `Protocol` | string | `"UDP"` | Transport: `UDP` or `TCP`. TCP uses RFC 6587 octet-counting framing. |
| `Facility` | int | `10` | RFC 5424 facility number. `10` = authpriv (security/auth). Common values: `4`=auth, `16`–`23`=local0–local7. |
| `AppName` | string | `"PassReset"` | APP-NAME field in the syslog header. |

Each syslog message follows RFC 5424 format with a structured-data element:
```
<priority>1 <timestamp> <hostname> PassReset - - - [PassReset@0 event="..." user="..." ip="..."]
```

### AlertEmail

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Set to `true` to enable email alerts. Requires `SmtpSettings.Host` to be set. |
| `Recipients` | string[] | `[]` | One or more recipient email addresses for alert messages. |
| `AlertOnEvents` | string[] | `["PortalLockout"]` | Event type names that trigger an email alert. |

### AlertOnEvents — valid event type names

| Event | When it fires |
|-------|---------------|
| `PasswordChanged` | Password changed successfully |
| `InvalidCredentials` | Wrong current password supplied |
| `UserNotFound` | Username not found in AD |
| `PortalLockout` | Portal lockout threshold reached |
| `ApproachingLockout` | One more wrong attempt will trigger portal lockout |
| `RateLimitExceeded` | Request rejected by the rate limiter (5 req / 5 min) |
| `RecaptchaFailed` | reCAPTCHA v3 validation failed |
| `ChangeNotPermitted` | User blocked by AD group allow/block list |
| `ValidationFailed` | Request rejected by model validation |
| `Generic` | Unexpected server-side error |
