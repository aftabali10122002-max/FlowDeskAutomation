# FlowDesk

**FlowDesk** is a full-stack approval management system built with ASP.NET Core Web API, Entity Framework Core, SQL Server, React, and TypeScript.

It is designed to manage approval requests, department-based workflows, multi-level approvals, and notifications.

## Features

### Approval Workflow
- Employee approval request submission
- Typed requests for material purchases, leave, expense reimbursement, IT access, travel, and work from home
- Employee self-service profile editing and authenticated profile photos
- Department-based approval management
- Multi-level approval workflow
- Configurable approval rules
- Request priority and deadlines
- Approval and rejection decisions
- Approval comments and workflow tracking

### Notifications — Phase 5
- Persisted, recipient-scoped notifications with paginated retrieval, unread counts, and mark-read.
- User-targeted SignalR delivery at `/hubs/notifications`; the connection uses the existing JWT.
- Assignment and workflow decision emails queued in a SQL Server outbox and sent asynchronously.
- Configurable deadline reminders and automatic overdue escalation processing.
- Requests without a matching workflow notify active Admins that manual assignment is needed; manual assignment and workflow decisions persist their notifications with the corresponding change.
- Frontend action toasts are temporary and session-scoped; persisted notifications remain available in the notification feed.

The Phase 5 migration source is `src/FlowDesk.Infrastructure/Persistence/Migrations/20261020231021_AddWorkflowNotifications.cs`. It has been generated and reviewed but **not applied** in this task. Apply it only through the normal reviewed deployment process to a verified target database.

The Employee Self-Service Portal schema migration is `src/FlowDesk.Infrastructure/Persistence/Migrations/20261021185821_AddEmployeeSelfServicePortal.cs`. Its review script is `src/FlowDesk.Infrastructure/Persistence/Migrations/AddEmployeeSelfServicePortal.review.sql`. The migration is generated for review only and has **not been applied**; it preserves existing requests, assignments, decisions, notifications, and workflow history.

### Leave Management — Phase 8 Backend
- Leave requests are resolved against published, effective-dated policies and preserve the policy selection for each requested date.
- The existing approval workflow remains in use. Requests without a matching workflow retain the existing Admin-assignment fallback.
- Pending requests reserve available leave; final decisions release reservations and record used or scheduled units. Pending cancellation releases reservations. Cancelling approved future leave requires a linked approval request through `POST /api/requests/{id}/cancellation-requests`.
- Employee endpoints: `GET /api/leave/types`, `GET /api/leave/me/balances`, and `GET /api/leave/me/history`.
- Admin endpoints under `/api/admin/leave` manage policies, policy assignments, employee metadata, leave types, balances, manual adjustments, and adjustment audit history. `GET /api/leave/manager/team` provides the persisted-role- and department-scoped manager overview.
- Accrual calculations support annual, monthly, and pay-period policies. A hosted maintenance worker converts due scheduled leave commitments, posts accruals, and processes carry-forward and expiry ledger entries hourly after a one-minute startup delay. It uses `Leave:BusinessTimeZoneId`, configured as `UTC` in `appsettings.json`. Repository tests cover the ledger processing rules; SQL Server integration and production concurrency behavior have not been verified.

The additive workflow-accounting migration is `src/FlowDesk.Infrastructure/Persistence/Migrations/20261023202154_Phase8LeaveWorkflowAccounting.cs`; its offline SQL review script is `src/FlowDesk.Infrastructure/Persistence/Migrations/Phase8LeaveWorkflowAccounting.review.sql`. The migration has **not been applied**. It adds leave request policy snapshots and cancellation/type references, backfills ledger effective dates from their UTC creation timestamps, and replaces a query index. Review and apply only through the normal deployment process to a verified target.

Email delivery is disabled unless `Email:Enabled` is set to `true`. Keep SMTP credentials in user-secrets or deployment environment variables, never in source-controlled settings:

```powershell
dotnet user-secrets set "Email:Enabled" "true" --project .\src\FlowDisk.Api
dotnet user-secrets set "Email:Smtp:Host" "<SMTP_HOST>" --project .\src\FlowDisk.Api
dotnet user-secrets set "Email:Smtp:Port" "587" --project .\src\FlowDisk.Api
dotnet user-secrets set "Email:Smtp:EnableSsl" "true" --project .\src\FlowDisk.Api
dotnet user-secrets set "Email:Smtp:From" "<SENDER_ADDRESS>" --project .\src\FlowDisk.Api
dotnet user-secrets set "Email:Smtp:Username" "<SMTP_USERNAME>" --project .\src\FlowDisk.Api
dotnet user-secrets set "Email:Smtp:Password" "<SMTP_PASSWORD>" --project .\src\FlowDisk.Api
```

SMTP username/password are optional for relays that do not require authentication. `Notifications:WorkerIntervalSeconds` defaults to `60`; `Notifications:ReminderMinutesBeforeDeadline` defaults to `60`. The reminder is sent once per active workflow stage. Failed email deliveries retry after one minute, up to ten attempts. SMTP cannot guarantee exactly-once delivery if the process stops after the remote server accepts a message but before the outbox is marked sent.

The frontend uses the API's same-origin SignalR endpoint. The Vite development proxy forwards `/hubs` with WebSocket support to `http://localhost:5192`. To manually verify, run the API and frontend, sign in as the intended approver, assign a request or submit a routed request, and observe the notification badge/list in that user's session. Then verify that another user's session receives no private notification and that a disconnected user can retrieve it after reconnecting or reopening the app.

## Technology Stack

### Backend
- ASP.NET Core Web API
- .NET 10
- Entity Framework Core
- SQL Server
- JWT authentication
- Role-based authorization

### Frontend
- React
- TypeScript
- Vite
- Vitest
- React Testing Library
- Oxlint

## Project Structure

```text
FlowDesk/
├── src/
│   ├── FlowDesk.Application/
│   ├── FlowDesk.Infrastructure/
│   └── FlowDisk.Api/
├── tests/
├── tools/
├── docs/
├── web/
├── FlowDesk.slnx
└── README.md
```

Directory names may differ slightly as the project evolves.

## Prerequisites

Install the following tools:

- .NET 10 SDK
- SQL Server, or Docker with the Compose plugin for the local container setup
- `sqlcmd` or another SQL Server client to initialize a local Compose database
- Node.js and npm
- Git

## Backend Setup

1. Clone the repository.
2. Open a terminal in the repository directory.
3. Configure the SQL Server connection string using .NET User Secrets.
4. Configure the required JWT settings using User Secrets.
5. Apply migrations only to a verified development or test database.
6. Run the API project.

Example configuration commands:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<YOUR_LOCAL_CONNECTION_STRING>" --project .\src\FlowDisk.Api

dotnet user-secrets set "Jwt:SecretKey" "<YOUR_RANDOM_DEVELOPMENT_SECRET>" --project .\src\FlowDisk.Api
```

Configure the JWT issuer, audience, and expiration according to the application's existing configuration.

Run the API:

```powershell
dotnet run --project .\src\FlowDisk.Api
```

Use the local URL printed by the application.

## Frontend Setup

Open a separate terminal:

```powershell
cd web
npm ci
npm run dev
```

### Frontend Validation

```powershell
npm run test
npm run lint
npm run build
```

## Backend Validation

From the repository root:

```powershell
dotnet restore .\FlowDesk.slnx
dotnet build .\FlowDesk.slnx --configuration Release --no-restore
dotnet test .\tests\FlowDisk.Api.Tests\FlowDisk.Api.Tests.csproj --configuration Release --no-build
dotnet test .\tests\FlowDisk.Api.Tests\FlowDisk.Api.Tests.csproj --configuration Release --collect:"XPlat Code Coverage"
```

The coverage command uses Coverlet's VSTest collector and writes Cobertura XML
under `tests/FlowDisk.Api.Tests/TestResults`. CI uploads this report as the
`backend-coverage` artifact; no coverage threshold is enforced yet. The two
SQL Server integration tests are skipped unless the separately provisioned,
explicitly configured test database is available. The PDF export coverage is
temporarily skipped while the CI-only HTTP 409 pagination condition is
investigated; a local SQLite pass does not resolve that CI failure.

## Dependency Management

NuGet restore lock files are generated for the .NET projects by
`Directory.Build.props` and are committed alongside their project files. They
record the resolved direct and transitive package versions, so local and CI
restores use the same dependency graph. CI uses locked-mode restore and fails
when a project change would require an unreviewed lock-file update.

After intentionally changing a NuGet `PackageReference`, regenerate and review
the lock files from the repository root:

```powershell
dotnet restore .\FlowDesk.slnx
git diff -- '**/packages.lock.json'
dotnet restore .\FlowDesk.slnx --locked-mode
```

Commit the project-file and corresponding lock-file changes together. Do not
edit lock-file dependency versions or content by hand. To audit direct and
transitive NuGet dependencies for known vulnerabilities:

```powershell
dotnet list .\FlowDesk.slnx package --vulnerable --include-transitive
```

The web dependencies are pinned by the committed `web/package-lock.json`.
From `web`, install exactly those resolved versions and audit high and critical
advisories with:

```powershell
npm ci
npm audit --audit-level=high
npm run lint
npm run test
npm run build
```

Dependabot checks the NuGet projects, npm lock file, and GitHub Actions weekly.
Compatible minor and patch updates are grouped into reviewable pull requests;
major updates remain separate, and no updates are automatically merged. Review
each update's manifest and lock-file diff, advisory details, and CI results
before merging. CI performs locked-mode NuGet restore, fails on high or
critical NuGet restore advisories, runs a transitive package audit, and audits
npm dependencies before frontend lint, tests, and build.

## Local API and SQL Server with Docker Compose

The Compose setup is for local development only. It binds both the API and SQL
Server ports to `127.0.0.1` (not to public interfaces), keeps database and
private attachment/profile-image files in named volumes, and never applies
migrations automatically. Do not use it as a production deployment template.

1. Install Docker Engine/Desktop with the Compose plugin. Copy the placeholder
   configuration and replace the password and JWT secret with local-only
   values:

   ```powershell
   Copy-Item .\.env.example .\.env
   ```

2. Start the isolated SQL Server service and check its health:

   ```powershell
   docker compose up --detach database
   docker compose ps
   ```

   The database health check confirms that SQL Server is listening on its
   private Compose network; it does not verify schema or migration state.

3. Initialize only the new local `FlowDeskLocal` database. First inspect and
   review the generated migration script; confirm the target is the Compose
   `database` service and `FlowDeskLocal`. The API does not run migrations at
   startup. Install the SQL Server command-line tools or use another approved
   local SQL Server client to apply the script. The EF design-time factory
   uses an offline-only connection, so script generation does not connect to
   SQL Server:

   ```powershell
   dotnet tool install --global dotnet-ef --version 10.0.0
   New-Item -ItemType Directory -Force .\artifacts | Out-Null
   dotnet ef migrations script --idempotent `
     --project .\src\FlowDesk.Infrastructure\FlowDesk.Infrastructure.csproj `
     --startup-project .\src\FlowDisk.Api\FlowDisk.Api.csproj `
     --output .\artifacts\FlowDeskLocal.sql
   ```

   Create the database and execute the reviewed script. `sqlcmd` prompts for
   the SA password; the Compose SQL port is loopback-only:

   ```powershell
   sqlcmd -S localhost,1433 -U sa -C -d master -Q "IF DB_ID('FlowDeskLocal') IS NULL CREATE DATABASE [FlowDeskLocal]" -b
   sqlcmd -S localhost,1433 -U sa -C -d FlowDeskLocal -i .\artifacts\FlowDeskLocal.sql -b
   ```

   If `FLOWDESK_SQL_PORT` in `.env` is changed, use that port in these commands.
   Choose a local SA password without semicolons because it is included in the
   API's SQL Server connection string.

   These commands are only for the fresh local Compose database. Never point
   the generated script or these commands at production or another existing
   database. Existing databases require the documented preflight, backup,
   migration review, and separate authorization process.

4. After the local migration completes, start the API and check health:

   ```powershell
   docker compose --profile local-app up --build --detach api
   docker compose ps
   ```

   The API liveness check is available at
   `http://localhost:8080/health/live`. It verifies process liveness, not
   database schema state.
   The local Compose stack does not provide a malware scanner; attachment
   downloads remain fail-closed until an approved scanner is configured.

5. Inspect logs and shut down without deleting persisted database or file
   data:

   ```powershell
   docker compose logs --follow api database
   docker compose down
   ```

   `docker compose down --volumes` permanently deletes the local database and
   private-file volumes; do not use it unless you intentionally want to erase
   this local development data.

## Database Safety

- Never commit database passwords, JWT secrets, API keys, or private certificates.
- Do not commit local database files or backups.
- Verify the target database before applying migrations.
- Keep development, testing, and production databases separate.
- Do not apply migrations to a production database without a reviewed deployment plan.

## Development Status

The project is under active development.

Phase 4 provides the advanced approval workflow. Phase 5 notification code and migration source are implemented; email delivery requires SMTP configuration, and the migration must be applied to the intended database before deployment.

## Contributing

1. Create a feature branch.
2. Make focused changes.
3. Add or update automated tests.
4. Run backend and frontend validation.
5. Submit a pull request describing the changes.

## License

No license has been specified yet. All rights are reserved unless a license is added to this repository.
#   F l o w D e s k  
 