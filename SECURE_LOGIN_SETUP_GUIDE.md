# Secure login setup guide

Eltse Hall uses application-owned accounts restricted to `@wmpenn.edu`. There is no public registration: an address with the correct domain can sign in only after an administrator or Hall Director provisions it.

## What the login system enforces

- Exact `@wmpenn.edu` email-domain validation on the server.
- ASP.NET Core Identity password hashing; plaintext passwords are never stored.
- Passwords between 15 and 128 characters, with support for long passphrases.
- Rejection of a small set of obvious William Penn/Eltse passwords.
- A generic failure for unknown users, external addresses, and wrong passwords.
- Five failed passwords trigger a 15-minute account lockout.
- Per-IP login throttling reduces automated guessing.
- Eight-hour browser sessions or optional 30-day persistent encrypted, HttpOnly, Secure authentication cookies.
- Antiforgery tokens on every state-changing browser request.
- Session invalidation after password, role, or active-status changes.
- A mandatory password change after an administrator-issued temporary password.

## Create the first administrator

Production has no default account. Bootstrap is available only while the account table is empty and requires a high-entropy secret stored in server configuration.

### 1. Generate a one-time token

Run locally in PowerShell:

```powershell
$tokenBytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($tokenBytes)
$bootstrapToken = [Convert]::ToBase64String($tokenBytes)
$bootstrapToken
```

Put the generated value in the server setting `Authentication__BootstrapToken`. Do not commit it, send it in chat/email, or reuse it as a password.

### 2. Choose the initial administrator password

Use a unique passphrase of 15–128 characters and save it in an approved password manager. Do not use the bootstrap token as this password.

### 3. Call the bootstrap endpoint

After the deployed application and database are ready, run this locally. Replace the four placeholder values:

```powershell
$appUrl = 'https://YOUR-APP.azurewebsites.net'
$adminEmail = 'YOUR-ADMIN@wmpenn.edu'
$adminFirstName = 'YOUR FIRST NAME'
$adminLastName = 'YOUR LAST NAME'
$adminPassword = 'A UNIQUE 15+ CHARACTER PASSPHRASE'

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$csrf = Invoke-RestMethod -Uri "$appUrl/api/auth/csrf" -WebSession $session
$headers = @{
  'X-CSRF-TOKEN' = $csrf.token
  'X-Bootstrap-Token' = $bootstrapToken
}
$body = @{
  email = $adminEmail
  firstName = $adminFirstName
  lastName = $adminLastName
  password = $adminPassword
} | ConvertTo-Json

Invoke-RestMethod -Uri "$appUrl/api/auth/bootstrap" `
  -Method Post `
  -WebSession $session `
  -Headers $headers `
  -ContentType 'application/json' `
  -Body $body
```

A successful response identifies the new administrator. Bootstrap will refuse another request after the first account exists.

### 4. Remove the token immediately

Delete `Authentication__BootstrapToken` from the hosting environment, save the configuration, and restart the application. Also clear the local PowerShell variable:

```powershell
$bootstrapToken = $null
```

Sign in through the normal web page to confirm the administrator account works.

## Add staff accounts

1. Sign in as an Admin or Hall Director.
2. Open **People**.
3. Select **Add account**.
4. Enter the staff member's name and exact `@wmpenn.edu` email.
5. Select the role and create the account.
6. Copy the generated temporary password once and provide it through an approved secure channel.

The application never displays that temporary password again. The staff member must replace it immediately after signing in.

Authorization rules:

- Admins can provision RAs, Hall Directors, and other Admins.
- Hall Directors can provision and manage RAs only.
- RAs cannot provision accounts.
- Nobody can use the account editor to deactivate themselves, demote themselves, or reset their own password.

If a staff record already exists because of scheduling or roster data, provisioning the same email attaches the login to that record instead of duplicating the person.

## Reset a forgotten or compromised password

1. Open **People**.
2. Find the user and choose **Reset password**.
3. Confirm the reset.
4. Copy the new temporary password once and send it securely.

Resetting a password invalidates the user's earlier sessions and requires another password change. Administrators should never ask users to reveal their chosen password.

## Change your own password

Open **Profile**, choose **Change password**, and enter the current and new passwords. If the current password is forgotten, another authorized staff member must perform the reset from **People**.

## Persistent sign-in

The login screen selects **Keep me signed in on this device** by default. On a private device, this keeps the encrypted session cookie for up to 30 days across browser restarts. Clear the option on a shared device. Signing out removes the cookie, and a password reset, password change, role change, or account deactivation invalidates it immediately.

## Deactivate or change access

Use **People** to change a role or mark an account inactive. Both actions invalidate its current session. Deactivation is preferable to deleting a staff record because audit history and completed dorm checks retain the responsible person's identity.

## Recovery if no administrator can sign in

Do not re-enable bootstrap against a populated database; it intentionally remains disabled. Use an approved operational recovery process:

1. Confirm the application is connected to the correct database.
2. Have an authorized database/application operator restore access using a controlled maintenance procedure.
3. Record who approved and performed the recovery.
4. Reset the recovered account password and review active users and recent audit activity.

For an institutional deployment, document this procedure with university IT before launch.

## Production checklist

- [ ] `Authentication__AllowedEmailDomain` is exactly `wmpenn.edu`.
- [ ] `Authentication__BootstrapToken` was removed after the first account.
- [ ] `SeedData__Enabled` is `false`.
- [ ] The site is served only over HTTPS.
- [ ] React and the API use the same production origin.
- [ ] Database backups are enabled and restore-tested.
- [ ] Dorm-check photo storage is durable and access-controlled.
- [ ] Temporary passwords are shared only through an approved secure channel.
- [ ] Departing staff are deactivated promptly.
- [ ] University IT has reviewed student-record retention and access requirements.

## Limits of application-owned authentication

This login does not verify current university employment and does not automatically receive university MFA, password resets, or account-disable events. Restricting the email suffix prevents external-domain accounts, but trusted app administrators still decide who receives an account. For long-term or campus-wide use, university-managed SSO with MFA is the stronger model and should be reviewed with IT.
