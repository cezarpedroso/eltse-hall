# Free student deployment guide (no Static Web Apps)

This deployment does **not** use Azure Static Web Apps. Create one regular **App Service > Web App** instead. The Release build places the React interface inside the ASP.NET Core application, so that single Web App serves both the screens and the API.

The recommended student pilot uses an App Service Free F1 plan and the Azure SQL Database free offer. Azure describes F1 as a trial/learning tier with no SLA and a daily CPU quota, so this is appropriate for a small hall pilot rather than a mission-critical production service. Check the choices and estimated monthly cost shown for your university subscription before pressing **Create**.

Useful official pages:

- [Azure for Students](https://azure.microsoft.com/en-us/free/students/)
- [Create an ASP.NET Core web app in Azure App Service](https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore)
- [App Service plan pricing and Free F1 limits](https://azure.microsoft.com/en-us/pricing/details/app-service/windows/)
- [Azure SQL Database free offer](https://azure.microsoft.com/en-us/products/azure-sql/database/)

## Architecture

```text
Phone or computer
        |
        | HTTPS, one origin
        v
Azure App Service
  - React files bundled into the .NET publish output
  - ASP.NET Core API
  - secure login cookies
        |
        v
Azure SQL Database
```

There is no separate frontend hosting resource in this diagram. Using one origin is important: mobile browsers do not need to accept a third-party authentication cookie, and the API can enforce strict same-site cookies and antiforgery protection.

## 1. Prerequisites

- A GitHub repository containing this project.
- An Azure account with permission to create a Resource Group, App Service, and Azure SQL resources.
- .NET 10 and Node.js 22 locally if you want to test the release build before deployment.
- A long, unique SQL administrator password stored in a password manager.

## 2. Test the production build locally

From the repository root:

```powershell
dotnet restore
npm ci --prefix src/raduty-web
dotnet test
npm run lint --prefix src/raduty-web
npm run test --prefix src/raduty-web
dotnet publish src/RaDuty.Api/RaDuty.Api.csproj --configuration Release --output publish
```

The Release publish target builds the frontend and copies it into the API's `wwwroot`. Do not deploy the frontend separately.

## 3. Create Azure SQL

1. In the Azure portal, create a Resource Group for the pilot.
2. Create an Azure SQL logical server and SQL Database in that group.
3. On the database configuration screen, select **Apply offer** for the Azure SQL Database free offer if it appears for your subscription. Confirm that the portal's estimated cost is zero before continuing.
4. Enable **Allow Azure services and resources to access this server** so App Service can connect.
5. Add your current client IP temporarily if you will apply migrations from your computer.
6. Copy the ADO.NET connection string and replace the password placeholder.

The connection string resembles:

```text
Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Persist Security Info=False;User ID=<sql-admin>;Password=<password>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

Do not put this value in source control.

## 4. Apply the database schema

Set the production connection string only for the current terminal and run the migrations:

```powershell
$env:ConnectionStrings__RaDuty = '<azure-sql-connection-string>'
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/RaDuty.Infrastructure --startup-project src/RaDuty.Api --configuration Release
Remove-Item Env:ConnectionStrings__RaDuty
```

The migration preserves existing hall residents, staff records, schedules, and dorm-check data while adding the application-owned login tables.

## 5. Create the App Service

1. In the portal, choose **Create a resource > Web App** under App Services. Do not choose **Static Web App**.
2. Put it in the same region and Resource Group as the database, select **Code** as the publish method, and choose Linux as the operating system.
3. Choose the .NET 10 runtime. If the portal does not list .NET 10, stop here rather than silently selecting an older runtime; this project targets .NET 10.
4. Under the App Service plan, choose **Free F1** and verify the estimated cost is zero. The free plan has no SLA and can stop serving requests after its daily quota is exhausted.
5. Turn on **HTTPS Only**.
6. Under **Configuration**, add these application settings:

| Name | Value |
|---|---|
| `ConnectionStrings__RaDuty` | The Azure SQL connection string |
| `Authentication__AllowedEmailDomain` | `wmpenn.edu` |
| `Authentication__BootstrapToken` | A one-time random token; remove after step 8 |
| `SeedData__Enabled` | `false` |
| `ResidenceHall__TimeZone` | `America/Chicago` |
| `DormCheckPhotos__StoragePath` | `/home/data/DormCheckPhotos` |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` |

Generate the bootstrap token locally rather than inventing one:

```powershell
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

Store that result temporarily in a password manager. The App Service name will determine the initial URL, for example `https://eltse-hall.azurewebsites.net`.

## 6. Keep photos and cookie keys durable

On Azure App Service, files below `/home` persist across application restarts. The photo setting above deliberately uses `/home/data`. ASP.NET Core also persists its Data Protection key ring under the App Service home directory for a single app/slot, allowing encrypted login cookies to survive normal restarts.

Do not scale this pilot to multiple independent hosts until the photo store and Data Protection key ring are moved to shared durable services.

## 7. Deploy from GitHub after merging the pull request

The simplest path is App Service **Deployment Center**. This connects the Web App to GitHub; it still does not create or require an Azure Static Web Apps resource:

1. Select GitHub as the source.
2. Select the `cezarpedroso/eltse-hall` repository and the `main` branch.
3. Let Azure create the GitHub Actions workflow.
4. Ensure the workflow installs Node.js 22 and .NET 10 before `dotnet publish`.
5. Deploy the output produced by:

   ```powershell
   dotnet publish src/RaDuty.Api/RaDuty.Api.csproj --configuration Release --output publish
   ```

6. Open the App Service URL. You should see the Eltse Hall login page.

If you maintain the workflow yourself, upload/deploy the whole `publish` folder. The React build is already inside it.

## 8. Create the first administrator

Follow [SECURE_LOGIN_SETUP_GUIDE.md](SECURE_LOGIN_SETUP_GUIDE.md). In short, fetch an antiforgery token and call the one-time bootstrap endpoint with:

- an `@wmpenn.edu` email address;
- a new password of 15-128 characters; and
- the temporary bootstrap token from App Service configuration.

As soon as the first administrator exists:

1. Delete `Authentication__BootstrapToken` from App Service configuration.
2. Save the configuration and restart the app.
3. Sign in as the administrator.
4. Use **People** to provision Hall Directors and RAs.

The bootstrap endpoint also disables itself when any login account exists. Removing the setting is still required so the secret is not retained unnecessarily.

## 9. Production verification

Verify all of the following before giving the link to staff:

- [ ] The site redirects HTTP to HTTPS.
- [ ] An unknown `@wmpenn.edu` address cannot sign in.
- [ ] A non-`@wmpenn.edu` address receives the same generic login failure.
- [ ] A provisioned user can sign in and is forced to replace a temporary password.
- [ ] An RA cannot open the administrative pages or edit another RA's schedule entries.
- [ ] A Hall Director can provision and reset RA accounts but cannot create an administrator.
- [ ] A role change, deactivation, or password reset invalidates the user's previous session.
- [ ] Dorm-check photos still load after an App Service restart.
- [ ] PDF exports download correctly on a phone.
- [ ] `Authentication__BootstrapToken` and `SeedData__Enabled=true` are absent from production.
- [ ] Database and application logs do not contain passwords, temporary passwords, or the bootstrap token.

## 10. Backups and maintenance

- Configure the database backup/retention options available for the selected Azure SQL tier.
- Periodically export critical reports and test a restore procedure.
- Keep the App Service, .NET packages, and npm dependencies patched.
- Deactivate accounts immediately when staff leave the hall.
- Reset a user's password from **People** if compromise is suspected; this invalidates existing sessions.
- Treat dorm-check notes and photos as sensitive student records. Limit access, retention, and downloads according to university policy.

## Troubleshooting

**The root URL is blank or returns 404**

Confirm the deployed output is the `dotnet publish` folder and contains `wwwroot/index.html`. The frontend is served by ASP.NET Core in production.

**The API cannot reach SQL**

Check `ConnectionStrings__RaDuty`, the SQL firewall setting, database name, credentials, and whether the schema migration completed.

**Login works locally but not after deployment**

Confirm the browser is using the HTTPS App Service URL and that the frontend was not deployed on a separate origin. Check the app logs for database or Data Protection errors without logging any submitted password.

**All accounts were lost after deployment**

Accounts live in Azure SQL, not in the App Service filesystem. Verify the deployed app is pointing to the intended persistent database rather than a new or local database.

**Free-tier limits stop the app**

Free hosting quotas can pause or stop an app. Review the App Service and Azure SQL metrics, then restart when quota permits or move to a paid tier if the pilot becomes operationally important.

## Important security boundary

This application-owned login is not university single sign-on and does not automatically inherit university MFA, account disablement, or password policy. The app's administrators are responsible for provisioning only authorized staff, removing access promptly, handling credentials securely, and following William Penn University policy. For a broader or long-term deployment, have university IT review the application and consider institution-managed SSO and MFA.
