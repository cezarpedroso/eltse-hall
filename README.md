# Night Desk — Resident Assistant duty scheduling

Night Desk is a production-oriented MVP for monthly Resident Assistant night-duty scheduling. It combines an ASP.NET Core API, SQL Server persistence, Microsoft Entra authorization, server-side PDF export, and a responsive React interface designed around a high-density monthly calendar and mobile agenda.

## What is implemented

- Microsoft Entra access-token validation with Microsoft.Identity.Web and MSAL React.
- Fail-closed approved-group authorization plus `ResidentAssistant` and `HallDirector` application roles.
- A development-only authentication scheme that cannot run outside the ASP.NET `Development` environment.
- Monthly schedule lifecycle: Draft → Open for Selection → Closed → Published → Archived, with guarded reverse transitions where operationally useful.
- Residence-hall timezone-aware generation with UTC persistence and real start/end timestamps. Friday and Saturday shifts end at 2:00 AM; all other nights end at midnight.
- Race-safe assignment with a serializable SQL transaction, row-version concurrency, and unique active-assignment constraints.
- Server-enforced monthly, weekend, consecutive-night, lock, capacity, activity, and schedule-state rules.
- Hall Director overrides, shift locking, lifecycle controls, user management, coverage distribution, unfilled-shift reporting, and audit history.
- Authorized RA directory and self-service contact profile.
- QuestPDF-based printable schedule export with page headers, footers, page counts, assignment details, and unfilled indicators.
- RFC 7807 Problem Details with stable codes such as `SHIFT_FULL`, `SHIFT_LOCKED`, `MAXIMUM_SHIFTS_REACHED`, and `CONCURRENCY_CONFLICT`.
- Seed data with one Hall Director, eight RAs, current/open and previous/published periods, assignments, openings, and locked shifts.
- Automated backend domain/security/PDF tests and frontend interaction/accessibility tests.

## Solution layout

```text
RaDuty.slnx
├─ src/
│  ├─ RaDuty.Domain/          entities, lifecycle, time generation, scheduling rules
│  ├─ RaDuty.Application/     DTOs, requests, service contracts, application errors
│  ├─ RaDuty.Infrastructure/  EF Core, SQL Server, services, PDF rendering, seed data
│  ├─ RaDuty.Api/             controllers, Entra auth, policies, middleware, OpenAPI
│  └─ raduty-web/             React, TypeScript, Vite, MSAL, TanStack Query
├─ tests/RaDuty.Tests/        xUnit domain, persistence, authorization, and PDF tests
├─ docker-compose.yml         local SQL Server 2022
└─ dotnet-tools.json          pinned EF Core CLI
```

The API controllers remain thin. Scheduling decisions live in domain rules and application services; database entities never leave the API directly. EF Core is used without a redundant generic repository layer.

## Prerequisites

- .NET SDK 10.0
- Node.js 22 or newer (24 is supported)
- Docker Desktop, or a reachable SQL Server/Azure SQL database
- For production authentication: a Microsoft Entra workforce tenant and permission to manage app registrations, enterprise applications, groups, and role assignments

## Local development in five steps

1. Restore the solution and frontend packages.

   ```powershell
   dotnet restore
   npm install --prefix src/raduty-web
   ```

2. Start SQL Server.

   ```powershell
   docker compose up -d sqlserver
   ```

3. Apply the migration. The API also migrates automatically when seed data is enabled in Development.

   ```powershell
   dotnet tool restore
   dotnet tool run dotnet-ef database update --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api
   ```

4. Copy the frontend example environment file and start both applications in separate terminals.

   ```powershell
   Copy-Item src/raduty-web/.env.example src/raduty-web/.env.local
   dotnet run --project src/RaDuty.Api --launch-profile https
   npm run dev --prefix src/raduty-web
   ```

5. Open `http://localhost:5173`. The default seeded identity is Jordan Lee, a Resident Assistant. To use the Hall Director seed identity, set `VITE_DEVELOPMENT_USER=director` in `.env.local` and restart Vite.

The browser variable only selects between server-defined test identities. It does not enable development authentication. The API enables that scheme only when `ASPNETCORE_ENVIRONMENT=Development` and server configuration explicitly enables `DevelopmentAuth`.

## Database migrations and seed data

Create a migration after changing entities:

```powershell
dotnet tool run dotnet-ef migrations add DescriptiveName --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api --output-dir Migrations
dotnet tool run dotnet-ef database update --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api
```

`SeedData:Enabled` is `true` only in `appsettings.Development.json`. `DevelopmentSeed.InitializeAsync` is idempotent and stops when a hall already exists. Disable it to develop against hand-managed data.

## Microsoft Entra setup

Use two single-tenant registrations: one for the API and one for the SPA.

### 1. Register and expose the API

1. Create an app registration such as `Night Desk API`, restricted to accounts in the university tenant.
2. Under **Expose an API**, accept an Application ID URI such as `api://<API_CLIENT_ID>`.
3. Add delegated scope `access_as_user` and record the full scope value.
4. Under **App roles**, add these enabled roles for `Users/Groups`:
   - Display name/value: `ResidentAssistant`
   - Display name/value: `HallDirector`
5. In **Token configuration**, add a `groups` claim for security groups. Prefer the option that emits only groups assigned to this application to avoid token overage.
6. In the enterprise application, set **Assignment required?** to Yes when the tenant policy allows it.

Microsoft documents the role-claim model in [Add app roles and get them from a token](https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-app-roles-in-apps) and protected API verification in [Verify scopes and app roles](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-protected-web-api-verification-scope-app-roles).

### 2. Register the SPA

1. Create `Night Desk Web` as a single-page application.
2. Add local redirect URI `http://localhost:5173` and the production HTTPS origin.
3. Add delegated permission to `Night Desk API / access_as_user` and grant tenant admin consent where required.
4. Put the SPA client ID, tenant ID, and API scope in the frontend deployment environment. These identifiers are public configuration; never put a client secret in React.

### 3. Configure the approved group and roles

1. Create or select the Residence Life security group.
2. Assign the group to the Night Desk enterprise application.
3. Assign either the `ResidentAssistant` or `HallDirector` app role to each authorized user or an appropriate group.
4. Set `Authorization:ApprovedGroupId` to the security group's object ID.

Both checks are required. A university email address alone grants nothing. The API takes the stable Entra `oid` claim as the external user key and never trusts a role sent by React.

If a user belongs to enough groups to trigger Entra group-claim overage, this MVP fails closed because the required group ID is absent. For a large tenant, either emit only groups assigned to the application or add an on-behalf-of Microsoft Graph membership resolver before launch; never treat an overage claim as authorization. Microsoft documents the JWT group limit and omission behavior in [Configure group claims](https://learn.microsoft.com/en-us/entra/identity/hybrid/connect/how-to-connect-fed-group-claims).

## Configuration reference

ASP.NET Core uses `__` to represent nested keys in environment variables.

| Setting | Environment variable | Purpose |
|---|---|---|
| `ConnectionStrings:RaDuty` | `ConnectionStrings__RaDuty` | SQL Server/Azure SQL connection string |
| `AzureAd:TenantId` | `AzureAd__TenantId` | University tenant ID |
| `AzureAd:ClientId` | `AzureAd__ClientId` | API application/client ID |
| `AzureAd:Audience` | `AzureAd__Audience` | Expected API audience |
| `Authorization:ApprovedGroupId` | `Authorization__ApprovedGroupId` | Required Residence Life group object ID |
| `AllowedOrigins:0` | `AllowedOrigins__0` | Exact SPA origin allowed by CORS |
| `ResidenceHall:TimeZone` | `ResidenceHall__TimeZone` | IANA timezone used when provisioning a hall |
| `PdfBranding:OrganizationName` | `PdfBranding__OrganizationName` | Printable organization name |
| `MicrosoftGraph:BaseUrl` | `MicrosoftGraph__BaseUrl` | Reserved Graph endpoint configuration |

Frontend build/runtime variables:

| Variable | Purpose |
|---|---|
| `VITE_API_BASE_URL` | HTTPS API origin |
| `VITE_ENTRA_CLIENT_ID` | SPA application/client ID |
| `VITE_ENTRA_TENANT_ID` | University tenant ID |
| `VITE_API_SCOPE` | Full delegated API scope |
| `VITE_USE_DEVELOPMENT_AUTH` | Skip MSAL UI locally; does not alter API security |
| `VITE_DEVELOPMENT_USER` | `ra` or `director` test identity selector |

For local secrets, avoid editing tracked configuration:

```powershell
dotnet user-secrets set "ConnectionStrings:RaDuty" "<local-connection-string>" --project src/RaDuty.Api
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>" --project src/RaDuty.Api
dotnet user-secrets set "AzureAd:ClientId" "<api-client-id>" --project src/RaDuty.Api
dotnet user-secrets set "Authorization:ApprovedGroupId" "<group-object-id>" --project src/RaDuty.Api
```

## API behavior

The requested endpoints are grouped under `/api/me`, `/api/schedules`, `/api/shifts`, `/api/resident-assistants`, and `/api/admin`. OpenAPI JSON is at `/openapi/v1.json` and intentionally requires the Hall Director policy. `/health` is the only anonymous endpoint.

Validation failures return `application/problem+json` with a stable `code` and trace ID. Scheduling rule violations use 422; races and concurrency conflicts use 409; authentication and authorization use 401/403.

Assignment claims run in a serializable database transaction. The database also enforces one active assignment per user/shift with a filtered unique index, while `DutyShift.RowVersion` protects administrative edits.

## Tests and production build

```powershell
dotnet test
npm run test --prefix src/raduty-web
npm run build --prefix src/raduty-web
```

The backend suite covers timestamp generation, weekend overnight handling, lifecycle transitions, group/role rules, scheduling limits, consecutive nights, lock/full/duplicate behavior, director override, concurrency modeling, inactive users, and PDF generation. The frontend suite covers calendar states, claiming, rule errors, role navigation, Director controls, mobile agenda structure, labels, and automated accessibility checks.

## Production deployment

For a complete project-specific zero-cost pilot walkthrough, see [FREE_DEPLOYMENT_GUIDE.md](FREE_DEPLOYMENT_GUIDE.md).

- Deploy the React `dist` directory to a static HTTPS host and the API to an ASP.NET-compatible host such as Azure App Service or a container platform.
- Use Azure SQL or managed SQL Server with encrypted connections, least-privilege credentials, automated backups, and private networking where practical.
- Run EF migrations as a controlled release step; disable `SeedData` and `DevelopmentAuth` in every non-Development environment.
- Store connection strings and any confidential credentials in a production secret store such as Azure Key Vault. The SPA must contain no secrets.
- Set exact HTTPS CORS origins; do not use wildcard origins with bearer-token APIs.
- Terminate TLS at the ingress, retain HSTS, preserve correlation IDs, and configure centralized structured-log retention without access tokens, phone numbers, or room numbers.
- Confirm your organization's QuestPDF license eligibility before deployment.
- Validate application-role and approved-group assignments in a staging tenant, including a user who has neither permission and a deactivated local user.
- Add an operational job or process for database backup, audit retention, and archive policy.

## Troubleshooting

**The API exits while seeding**  
Confirm SQL Server is healthy with `docker compose ps`, verify port 1433 is free, and check that the local password matches `appsettings.Development.json`.

**The browser reports a certificate or network error**  
Trust the local .NET development certificate with `dotnet dev-certs https --trust`, then restart the API and Vite.

**A signed-in user gets 403**  
Inspect the API access token (never paste it into logs or tickets) and confirm it contains the approved group object ID and exactly one supported role value. Confirm the local user and hall membership are active.

**A role assignment does not appear immediately**  
Entra-issued tokens can remain cached. Sign out, clear the SPA session, and sign in again after the administrator completes the assignment.

**A highly connected user gets 403 despite group membership**  
This is likely group-claim overage. Configure Entra to emit only groups assigned to the application or implement and review the Graph on-behalf-of extension described above.

**A claim returns 409 or 422**  
409 indicates another request won a race or an administrative edit used a stale row version. Refresh the schedule. A 422 response includes a stable rule code explaining the scheduling constraint.

**PDF generation fails in a container**  
QuestPDF bundles native dependencies for common platforms. Confirm the deployment image and architecture are supported and that the process can write temporary runtime files if the host requires them.
