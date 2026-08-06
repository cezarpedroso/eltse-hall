# Free deployment guide for Eltse Hall

Last verified: August 6, 2026

This guide deploys the app as a low-traffic pilot with no recurring hosting charge while it stays inside the providers' free limits:

```text
Phone or computer
        |
        v
Azure Static Web Apps Free (React site)
        |
        v
Azure App Service F1 (.NET API) ----> /home/data (room-check photos)
        |
        v
Azure SQL Database free offer (application data)
        |
        v
Microsoft Entra ID (university sign-in and roles)
```

## Read this before deploying

This is a **pilot/demo configuration, not a production-grade free hosting plan**. Azure explicitly describes App Service F1 as a trial/learning tier with no SLA and says it is not supported for production workloads. It provides 60 CPU minutes per day and 1 GB of storage. Azure Static Web Apps Free also has no SLA. The Azure SQL free offer is free without a time limit while the database stays inside its monthly limits, but it also has no SLA. See the official [App Service Linux pricing](https://azure.microsoft.com/en-us/pricing/details/app-service/linux/), [Static Web Apps plans](https://learn.microsoft.com/en-us/azure/static-web-apps/plans), and [Azure SQL free-offer FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql).

This app contains student names, room assignments, room-check responses, notes, and possibly photographs. Obtain approval from university IT or the office responsible for student-data handling before putting real information online. For an official university deployment, ask IT for an institution-managed Azure subscription and move the API to a supported paid tier with managed backups and durable photo storage.

### Free-tier limits that matter here

| Component | Free allowance | Practical effect |
|---|---:|---|
| Static Web Apps Free | 250 MB per app, 100 GB bandwidth/month, 2 custom domains | More than enough for this React build; no SLA. |
| App Service F1 | 60 CPU minutes/day, 1 GB RAM, 1 GB storage | The API can sleep, start slowly, or stop for the day after its CPU allowance is consumed. |
| Azure SQL free offer | 100,000 vCore-seconds/month, 32 GB data, 32 GB backup | Choose **Auto-pause until next month** to prevent overage charges. The database can be unavailable after the allowance is exhausted. |
| App Service photo storage | Shares the F1 plan's 1 GB | Four 5 MB photos per room check can fill it quickly. F1 does not include App Service backup/restore. |

Azure documents the Static Web Apps quotas [here](https://learn.microsoft.com/en-us/azure/static-web-apps/quotas), the SQL allowance [here](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), and App Service backup tier requirements [here](https://learn.microsoft.com/en-us/azure/app-service/manage-backup).

## What you need

- An Azure account with an active subscription. Azure may require a payment method even when the selected resources are free.
- Permission to create resources in that subscription.
- A GitHub account and a private repository for this project.
- Permission in the university Microsoft Entra tenant to create app registrations, expose an API, assign app roles, and configure group claims. University IT may need to do this part.
- .NET 10 and Node.js 22 or newer on the computer used for the initial database migration.
- A password manager for the temporary SQL administrator password.

Do not commit the resident spreadsheet, a database connection string, `.env.development.local`, access tokens, or Azure publish credentials to GitHub.

## Values worksheet

Record these values while working. Do not put the SQL password in this table if the guide will be shared.

| Placeholder used below | Example | Your value |
|---|---|---|
| `<RESOURCE_GROUP>` | `rg-eltse-hall` | |
| `<REGION>` | `Central US` | |
| `<SQL_SERVER>` | `eltse-hall-sql-abc123` | |
| `<DATABASE>` | `RaDuty` | |
| `<API_APP>` | `eltse-hall-api-abc123` | |
| `<API_URL>` | `https://eltse-hall-api-abc123.azurewebsites.net` | |
| `<WEB_URL>` | `https://kind-tree-123.azurestaticapps.net` | |
| `<TENANT_ID>` | Entra directory ID | |
| `<API_CLIENT_ID>` | API app registration ID | |
| `<SPA_CLIENT_ID>` | Web app registration ID | |
| `<APPROVED_GROUP_ID>` | Residence Life security-group object ID | |

Use the same Azure region for App Service and Azure SQL when possible.

## 1. Put the project in a private GitHub repository

From the project directory, run the local checks first:

```powershell
dotnet test
npm ci --prefix src/raduty-web
npm run lint --prefix src/raduty-web
npm run test --prefix src/raduty-web
npm run build --prefix src/raduty-web
```

Create a private repository on GitHub, then push this project to its `main` branch. Check GitHub's **Files changed** view before the first push. Confirm that no `.env*.local`, workbook, database file, photo, or password is present.

The project includes `src/raduty-web/public/staticwebapp.config.json`. Azure copies it into the built site so refreshing `/schedule`, `/residents`, or `/dorm-checks` continues to work. This follows Azure's documented [single-page application fallback configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/configuration#fallback-routes).

## 2. Create the free Azure SQL database

1. Sign in to the [Azure portal](https://portal.azure.com/).
2. Search for **SQL databases**, select **Create**, and create resource group `<RESOURCE_GROUP>`.
3. Set the database name to `<DATABASE>`.
4. Create a new logical SQL server named `<SQL_SERVER>` in `<REGION>`.
5. Choose **SQL authentication** for this pilot, create a unique administrator username, and generate a long password in a password manager.
6. Under **Compute + storage**, select the **Free offer**. Confirm the cost summary says zero and select **Auto-pause the database until next month** when its free limit is reached. Do not select the option that continues with paid usage.
7. Use locally redundant backup storage and finish creating the database.
8. Open the logical SQL server's **Networking** page. Set public access to **Selected networks**, select **Add your client IPv4 address**, and save. Do not enable “Allow Azure services and resources to access this server”; Microsoft notes that this opens the firewall to resources in other customers' Azure subscriptions too. See [Azure SQL firewall rules](https://learn.microsoft.com/en-us/azure/azure-sql/database/firewall-configure?view=azuresql).
9. On the database's **Connection strings** page, copy the ADO.NET SQL-authentication connection string and replace its password placeholder locally. Do not save it in a project file.

If the portal does not show **Free offer applied** and an estimated monthly cost of zero, stop instead of creating the database. Microsoft's current FAQ says the **Azure for Students Starter** subscription is not eligible for this SQL offer; use an eligible Azure Free/Azure for College Students subscription or ask university IT for an institutional subscription.

## 3. Create the free .NET API host

1. In Azure, search for **App Services**, select **Create** > **Web App**, and choose `<RESOURCE_GROUP>`.
2. Enter the globally unique name `<API_APP>`.
3. Set **Publish** to **Code**, runtime to **.NET 10**, operating system to **Linux**, and region to `<REGION>`.
4. Create an App Service plan and select **Free F1**. Skip Application Insights for the zero-cost pilot.
5. Create the app. Its address is `<API_URL>`.

.NET 10 and the F1 choice are supported in Microsoft's current [ASP.NET App Service quickstart](https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore).

### Restrict the SQL firewall to the API

1. Open the new App Service, then **Settings** > **Properties**.
2. Copy every address shown under **Outbound IP Addresses**. App Service can choose any address in this list at runtime, so all of them must be allowed. Microsoft explains this behavior in [App Service inbound and outbound addresses](https://learn.microsoft.com/en-us/azure/app-service/overview-inbound-outbound-ips).
3. Return to the Azure SQL logical server, open **Networking**, and add a single-IP firewall rule for each App Service outbound address.
4. Keep the temporary client-IP rule until database setup is complete.

If you later change the App Service tier, recheck the possible outbound addresses before expecting database access to work.

## 4. Configure Microsoft Entra sign-in

Use two single-tenant app registrations: one for the API and one for the React single-page app.

### API registration

1. In the Microsoft Entra admin center, create a single-tenant app registration named **Eltse Hall API**.
2. Record its Application (client) ID as `<API_CLIENT_ID>` and the directory ID as `<TENANT_ID>`.
3. Under **Expose an API**, set the Application ID URI to `api://<API_CLIENT_ID>`.
4. Add delegated scope `access_as_user`.
5. Under **App roles**, create these enabled roles for **Users/Groups**, using the exact value shown:
   - `ResidentAssistant`
   - `HallDirector`
   - `Admin`
6. Under **Token configuration**, add a **groups** claim for security groups.

### Web registration

1. Create a second single-tenant app registration named **Eltse Hall Web**.
2. Record its client ID as `<SPA_CLIENT_ID>`.
3. You will add `<WEB_URL>` as a **Single-page application** redirect URI after creating the static site in the next section.
4. Under **API permissions**, add the delegated `Eltse Hall API / access_as_user` permission and grant tenant admin consent.

### Authorize users

1. Create or choose one security group containing only approved Residence Life users. Record its object ID as `<APPROVED_GROUP_ID>`.
2. In the **Eltse Hall API** enterprise application, assign each person exactly one application role: `ResidentAssistant`, `HallDirector`, or `Admin`.
3. Ensure the same people are members of the approved security group.
4. Assign your own account `HallDirector` or `Admin` for the initial setup.

The API requires both the approved-group claim and a supported role; an email address alone is not authorization. Microsoft documents [app roles](https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-app-roles-in-apps) and [SPA redirect URIs](https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-redirect-uri).

## 5. Create the free React host

1. In the Azure portal, search for **Static Web Apps**, select **Create**, and choose `<RESOURCE_GROUP>`.
2. Select the **Free** plan and connect the private GitHub repository and `main` branch.
3. For build details, choose **Custom** and enter:

   | Setting | Value |
   |---|---|
   | App location | `/src/raduty-web` |
   | API location | leave empty |
   | Output location | `dist` |

4. Create the resource. Azure adds a GitHub Actions workflow and gives the site a URL; record it as `<WEB_URL>`.
5. Return to the **Eltse Hall Web** app registration and add `<WEB_URL>` as a **Single-page application** redirect URI. Add the same address as the front-channel logout URL if your tenant uses it. Redirect URIs must exactly match a registered address.

Azure documents the `app_location` and `output_location` fields in [Static Web Apps build configuration](https://learn.microsoft.com/en-us/azure/static-web-apps/build-configuration).

### Add the frontend build values

Vite embeds these public identifiers into the JavaScript bundle at build time. They are not passwords, and the frontend must never contain a client secret.

In GitHub, open **Settings** > **Secrets and variables** > **Actions** > **Variables**, then add:

| GitHub variable | Value |
|---|---|
| `VITE_API_BASE_URL` | `<API_URL>` |
| `VITE_ENTRA_CLIENT_ID` | `<SPA_CLIENT_ID>` |
| `VITE_ENTRA_TENANT_ID` | `<TENANT_ID>` |
| `VITE_API_SCOPE` | `api://<API_CLIENT_ID>/access_as_user` |
| `VITE_USE_DEVELOPMENT_AUTH` | `false` |

Open the workflow Azure created under `.github/workflows/`. In its **Build And Deploy** step, add this `env` block at the same indentation level as `uses` and `with`:

```yaml
- name: Build And Deploy
  uses: Azure/static-web-apps-deploy@v1
  env:
    VITE_API_BASE_URL: ${{ vars.VITE_API_BASE_URL }}
    VITE_ENTRA_CLIENT_ID: ${{ vars.VITE_ENTRA_CLIENT_ID }}
    VITE_ENTRA_TENANT_ID: ${{ vars.VITE_ENTRA_TENANT_ID }}
    VITE_API_SCOPE: ${{ vars.VITE_API_SCOPE }}
    VITE_USE_DEVELOPMENT_AUTH: ${{ vars.VITE_USE_DEVELOPMENT_AUTH }}
  with:
    # Keep the token and repository values generated by Azure.
    app_location: "/src/raduty-web"
    api_location: ""
    output_location: "dist"
```

Keep Azure's generated token/repository lines inside `with`; the abbreviated example intentionally does not reproduce them. Commit the workflow change or rerun it after editing.

## 6. Configure the API securely

Open `<API_APP>` in Azure, then **Settings** > **Environment variables** > **App settings**. Add the following. App Service exposes these as environment variables, which override `appsettings.json`; Microsoft documents that behavior [here](https://learn.microsoft.com/en-us/azure/app-service/configure-common).

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__RaDuty` | Azure SQL connection string |
| `AzureAd__TenantId` | `<TENANT_ID>` |
| `AzureAd__ClientId` | `<API_CLIENT_ID>` |
| `AzureAd__Audience` | `api://<API_CLIENT_ID>` |
| `Authorization__ApprovedGroupId` | `<APPROVED_GROUP_ID>` |
| `AllowedOrigins__0` | `<WEB_URL>` without a trailing slash |
| `ResidenceHall__TimeZone` | `America/Chicago` |
| `DormCheckPhotos__StoragePath` | `/home/data/DormCheckPhotos` |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE` | `true` |
| `DevelopmentAuth__Enabled` | `false` |
| `SeedData__Enabled` | `false` |
| `PdfBranding__OrganizationName` | your university or Residence Life name |

Save the settings. The app restarts automatically. Files written under `/home` persist across ordinary Linux App Service restarts when App Service storage is enabled, as explained in Microsoft's [App Service Linux storage FAQ](https://learn.microsoft.com/en-us/troubleshoot/azure/app-service/faqs-app-service-linux-new). They are still not a substitute for an independent backup.

## 7. Create the production schema and Eltse rooms

The API intentionally does not run migrations or development seed data in Production. Apply the checked-in migrations from your local project directory:

```powershell
dotnet tool restore
$dbConnection = Read-Host 'Paste the Azure SQL connection string'
dotnet tool run dotnet-ef database update `
  --project src/RaDuty.Infrastructure `
  --startup-project src/RaDuty.Api `
  --connection "$dbConnection"
Remove-Variable dbConnection
```

Using `Read-Host` keeps the password itself out of PowerShell command history. EF Core documents reviewed production migration options and migration bundles in [Applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

Next, open the Azure SQL database's **Query editor**, sign in with the SQL administrator, copy all of `deployment/bootstrap-eltse.sql`, and select **Run**. Its final result must show:

```text
Eltse Hall | America/Chicago | 100
```

The bootstrap is idempotent: rerunning it keeps one active Eltse Hall and creates only missing rooms. It does not insert fake users, schedules, room checks, or residents.

After this succeeds, remove the temporary local-client IP firewall rule from the SQL server. Keep the API outbound-IP rules.

## 8. Deploy the API from GitHub

1. Open the App Service's **Deployment Center**.
2. Select **GitHub**, authorize it, and choose the repository and `main` branch.
3. Use the portal's generated GitHub Actions authentication. Keep the generated Azure login/identity steps; do not paste a publish profile into source code.
4. Because this repository contains several .NET projects, edit the generated workflow's publish and deploy portion so it publishes only the API:

```yaml
- name: Publish API
  run: dotnet publish src/RaDuty.Api/RaDuty.Api.csproj --configuration Release --output ./publish

- name: Deploy API
  uses: azure/webapps-deploy@v3
  with:
    app-name: ${{ env.AZURE_WEBAPP_NAME }}
    package: ./publish
```

Keep the authentication inputs generated by Azure. If the workflow uses a literal app name instead of `AZURE_WEBAPP_NAME`, keep that generated value. Microsoft's supported workflow is described in [Deploy App Service with GitHub Actions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions).

Commit the workflow edit and watch the repository's **Actions** tab until both the API and static-site workflows are green.

## 9. First sign-in and roster setup

1. Open `<API_URL>/health`. It should return `Healthy` without signing in.
2. Open `<WEB_URL>` in a private browser window and sign in with the Hall Director/Admin account configured earlier.
3. The first successful API request creates that person's local account and Eltse Hall membership from the trusted Entra claims.
4. Open **Roster import**, upload the official resident workbook, review the analysis, and apply it. Do not upload the workbook to GitHub.
5. Open **Residents** and confirm the total and sample room assignments.
6. In the admin schedule area, create/open the current month's schedule if one does not exist.
7. Have one RA sign in and verify that the RA can manage residents and dorm checks but cannot access the spreadsheet-import or admin screens.

## 10. Launch checklist

- [ ] `<API_URL>/health` returns `Healthy`.
- [ ] Opening `<API_URL>/api/me` without a bearer token returns `401`.
- [ ] Sending an `X-Dev-User` header in Production still returns `401`.
- [ ] The website signs in through the university tenant.
- [ ] A user outside `<APPROVED_GROUP_ID>` receives `403`.
- [ ] RA, Hall Director, and Admin roles show the correct navigation.
- [ ] Refreshing `<WEB_URL>/schedule` and `<WEB_URL>/dorm-checks` does not return `404`.
- [ ] The resident spreadsheet preview and import work for a Hall Director/Admin.
- [ ] An RA cannot use spreadsheet import.
- [ ] A test room check, photo upload/download, and PDF export work.
- [ ] The SQL server no longer allows your temporary local IP.
- [ ] `DevelopmentAuth__Enabled` and `SeedData__Enabled` are both `false`.
- [ ] Azure Cost Analysis shows the expected free resources.

## Updating the deployed app

Normal code changes are deployed by pushing to `main`; GitHub Actions rebuilds the API and site.

If an entity changes, create and review an EF migration locally, back up what you can, apply the migration to Azure SQL, and then deploy the compatible API version. Never enable development seeding against the deployed database.

Frontend `VITE_*` values are build-time values. After changing a GitHub variable, rerun the Static Web Apps workflow. API environment-variable changes restart App Service automatically.

## Staying at zero charge

1. Confirm App Service remains on **F1** and Static Web Apps remains on **Free**.
2. Confirm the SQL database says **Free offer applied** and **Auto-pause until next month**.
3. Do not enable paid SQL overages, Application Insights ingestion, a paid custom API domain, Azure Storage mounts, private endpoints, or a paid App Service tier unless you intend to pay.
4. In Azure Cost Management, create a small monthly budget alert. A budget sends notifications; it does not automatically stop spending.
5. Monitor App Service CPU time, App Service disk usage, SQL free-vCore usage, and SQL storage.
6. Delete test resources you no longer use.

The free App Service API must use its `azurewebsites.net` address; custom App Service domains require a paid tier. The free Static Web App can use up to two custom domains, but changing the web origin requires updating the Entra SPA redirect URI, `AllowedOrigins__0`, and rebuilding the frontend.

## Known free-tier limitations and recovery

### Slow first request

F1 and serverless SQL can both sleep. The first request may take tens of seconds while the API and database wake up. Retry once before treating it as an outage.

### API returns 403 after working earlier

Check the F1 CPU quota first. When it is exhausted, the app can remain stopped until the daily allowance resets. Also sign out and back in after Entra group or role changes so the browser receives a fresh token.

### Database is unavailable

Check the SQL database's **Free amount remaining** metric. With the no-charge option selected, it pauses until the next calendar month after the allowance is exhausted.

### Photos disappear or storage fills

F1 includes only 1 GB and does not include App Service backup/restore. Delete unnecessary test photos and establish an institution-approved backup process before real use. The durable production fix is object storage such as Azure Blob Storage plus a paid, supported hosting plan; that requires an application change and is outside this zero-cost pilot.

### Sign-in returns `AADSTS50011`

The browser origin does not exactly match the SPA redirect URI. Copy `<WEB_URL>` from Azure Static Web Apps and add that exact HTTPS origin to the **Eltse Hall Web** registration.

### Signed-in user receives 403

Verify all three conditions:

1. The user has an API app-role assignment with one exact value: `ResidentAssistant`, `HallDirector`, or `Admin`.
2. The user belongs to `<APPROVED_GROUP_ID>`.
3. The API's `Authorization__ApprovedGroupId` contains that group's object ID.

Users with very large group memberships can hit Entra group-claim overage; this app fails closed when the required group claim is absent. Ask university IT to configure the group claim appropriately rather than weakening the API check.
