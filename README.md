# Eltse Hall residence-life tools

Eltse Hall is a responsive residence-life application for night-duty scheduling, resident room management, dorm checks, photographs, and PDF reporting. It uses an ASP.NET Core API, SQL Server, and a React/TypeScript interface designed for phones first.

## What is implemented

- A private, application-owned login system restricted to provisioned `@wmpenn.edu` accounts.
- Password hashing through ASP.NET Core Identity's versioned password hasher; plaintext passwords are never stored.
- Optional 30-day persistent HttpOnly sessions, CSRF protection, per-IP login throttling, and a 15-minute account lockout after five failed passwords.
- No public registration. A one-time production bootstrap creates the first administrator; Hall Directors and administrators provision everyone else.
- Server-generated temporary passwords that must be replaced at first sign-in.
- `ResidentAssistant`, `HallDirector`, and `Admin` authorization enforced by the API.
- A phone-friendly monthly night-duty calendar showing every assignee while allowing RAs to change only their own assignments.
- Dorm checks for 25 suites and four rooms per suite, resident names, photos, reset confirmation, and PDF export.
- Resident creation, editing, transfer, removal, and Excel roster analysis/import for Eltse Hall.
- Administrative scheduling, account management, password reset, activity history, and PDF reports.
- SQL persistence, optimistic concurrency, audit logs, validation, and automated backend/frontend tests.

## Security model

An email ending in `@wmpenn.edu` is necessary but is not sufficient by itself. The database must already contain a provisioned login account and an active Eltse Hall membership. There is no public sign-up endpoint, so an unknown person cannot gain access merely by entering a university-looking address.

User-chosen passwords must contain 15–128 characters. The app accepts long passphrases and does not impose arbitrary character-composition rules. Obvious Eltse/William Penn passwords are rejected. Login failures use a generic response to reduce account discovery.

The browser receives an encrypted, tamper-resistant authentication cookie with `HttpOnly`, `Secure`, and host-only protections in production. **Keep me signed in** makes it persistent for 30 days; otherwise it expires after eight hours or when the browser session ends. Every state-changing browser request also needs a server-issued antiforgery token. Role changes, deactivation, password resets, and password changes invalidate prior sessions.

For production, the React build and API are served by the same ASP.NET host. Keeping them on one HTTPS origin avoids third-party-cookie failures on mobile browsers.

## Solution layout

```text
RaDuty.slnx
├─ src/
│  ├─ RaDuty.Domain/          entities and scheduling rules
│  ├─ RaDuty.Application/     DTOs, contracts, and application errors
│  ├─ RaDuty.Infrastructure/  EF Core, Identity accounts, services, PDFs, and seed data
│  ├─ RaDuty.Api/             controllers, cookies, CSRF, policies, and middleware
│  └─ raduty-web/             React, TypeScript, Vite, and TanStack Query
├─ tests/RaDuty.Tests/        domain, persistence, authentication, and PDF tests
├─ deployment/                production database bootstrap
└─ docker-compose.yml         local SQL Server 2022
```

## Prerequisites

- .NET SDK 10.0
- Node.js 22 or newer
- Docker Desktop, or another reachable SQL Server

## Local development

1. Restore packages.

   ```powershell
   dotnet restore
   npm install --prefix src/raduty-web
   ```

2. Start SQL Server and apply the database migrations.

   ```powershell
   docker compose up -d sqlserver
   dotnet tool restore
   dotnet tool run dotnet-ef database update --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api
   ```

3. Start the API and web app in separate terminals.

   ```powershell
   dotnet run --project src/RaDuty.Api --launch-profile https
   npm run dev --prefix src/raduty-web
   ```

4. Open `http://localhost:5173`.

The development seed creates these useful accounts:

| Role | Email | Development-only temporary password |
|---|---|---|
| Hall Director | `carol.ocker@wmpenn.edu` | `eltse123` |
| Resident Assistant | `jennierobison@wmpenn.edu` | `eltse123` |
| Resident Assistant | `drakehamm@wmpenn.edu` | `eltse123` |
| Resident Assistant | `lillianzapata@wmpenn.edu` | `eltse123` |
| Resident Assistant | `madelynnzehr@wmpenn.edu` | `eltse123` |
| Resident Assistant | `madisongustafson@wmpenn.edu` | `eltse123` |
| Resident Assistant | `gavinhuff@wmpenn.edu` | `eltse123` |
| Admin | `cezarpedroso@wmpenn.edu` | `eltse123` |

The development-only temporary password must be replaced at first sign-in. It exists only in `appsettings.Development.json`; production disables development seed data and uses generated temporary passwords.

Vite proxies `/api`, `/health`, and `/openapi` to `https://localhost:7149`, so the browser uses the same-origin cookie flow during local development. If the API certificate is not trusted, run:

```powershell
dotnet dev-certs https --trust
```

## Production account setup

Production does not create a default password or public registration page. Follow [SECURE_LOGIN_SETUP_GUIDE.md](SECURE_LOGIN_SETUP_GUIDE.md) to:

1. Generate a one-time bootstrap token.
2. Create the first `@wmpenn.edu` administrator after the database is ready.
3. Remove the bootstrap token from hosting configuration.
4. Provision RAs and Hall Directors from **People**.

## Configuration

ASP.NET Core uses double underscores for nested environment-variable keys.

| Setting | Environment variable | Purpose |
|---|---|---|
| `ConnectionStrings:RaDuty` | `ConnectionStrings__RaDuty` | SQL Server/Azure SQL connection string |
| `Authentication:AllowedEmailDomain` | `Authentication__AllowedEmailDomain` | Must remain `wmpenn.edu` |
| `Authentication:BootstrapToken` | `Authentication__BootstrapToken` | One-time first-admin token; remove immediately afterward |
| `AllowedOrigins:0` | `AllowedOrigins__0` | Local/separate frontend origin if one is deliberately used |
| `ResidenceHall:TimeZone` | `ResidenceHall__TimeZone` | Hall timezone, normally `America/Chicago` |
| `DormCheckPhotos:StoragePath` | `DormCheckPhotos__StoragePath` | Durable room-check photo location |
| `PdfBranding:OrganizationName` | `PdfBranding__OrganizationName` | PDF organization name |
| `SeedData:Enabled` | `SeedData__Enabled` | Must be `false` in production |

Frontend configuration:

| Variable | Purpose |
|---|---|
| `VITE_API_BASE_URL` | Optional API origin. Leave empty for the recommended same-origin deployment. |

No client ID, tenant ID, OAuth scope, or browser secret is required.

## Database migrations

After changing an entity:

```powershell
dotnet tool run dotnet-ef migrations add DescriptiveName --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api --output-dir Migrations
dotnet tool run dotnet-ef database update --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api
```

The local-account migration removes the former external-identity column and creates the Identity account, claim, login, and reset-token tables. Existing staff/user records remain intact; an administrator can provision a login for an existing matching school email.

## Tests and build

```powershell
dotnet test
npm run lint --prefix src/raduty-web
npm run test --prefix src/raduty-web
npm run build --prefix src/raduty-web
```

A Release publish automatically installs frontend packages, builds React, and includes `dist` under the API's `wwwroot`:

```powershell
dotnet publish src/RaDuty.Api/RaDuty.Api.csproj --configuration Release --output publish
```

## Deployment

Follow [FREE_DEPLOYMENT_GUIDE.md](FREE_DEPLOYMENT_GUIDE.md) for the Azure student pilot walkthrough. It does not require Azure Static Web Apps: the React site and API deploy together on one Free F1 App Service origin, with Azure SQL, schema setup, first-administrator bootstrap, and bootstrap-secret removal.

## Authentication troubleshooting

**Every login says the email or password is incorrect**

Confirm the account was provisioned under **People**, the email ends with exactly `@wmpenn.edu`, and the password is correct. Unknown, external, and incorrect-password attempts intentionally return the same message.

**The account is temporarily locked**

Wait 15 minutes. Five failed passwords lock that account even when the correct password is subsequently entered. IP throttling can independently return HTTP 429.

**A temporary password works but the app remains blocked**

Complete the forced password-change screen. Normal APIs reject a temporary-password session until the replacement succeeds.

**A user was deactivated or changed roles but still had the app open**

The next API request validates the session against the database. Security-stamp changes invalidate prior cookies and require a new login.

**State-changing requests return HTTP 400**

Confirm the browser accepts the authentication and antiforgery cookies and that the frontend and API share one HTTPS origin in production. The frontend automatically refreshes its CSRF token once when a session changes.

**Deployment restarts sign everyone out**

Authentication cookies rely on ASP.NET Core Data Protection keys. Azure App Service persists these under its home directory for a single deployment slot. Containers or multiple independent hosts need a shared, durable, protected key ring.
