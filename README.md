# FlowDesk

**FlowDesk** is a full-stack approval management system built with ASP.NET Core Web API, Entity Framework Core, SQL Server, React, and TypeScript.

It is designed to manage approval requests, department-based workflows, multi-level approvals, and notifications.

## Features

### Approval Workflow
- Employee approval request submission
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

The Phase 5 migration source is `src/FlowDesk.Infrastructure/Persistence/Migrations/20261020231021_AddWorkflowNotifications.cs`. It has been generated and reviewed but **not applied** in this task. Apply it only through the normal reviewed deployment process to a verified target database.

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
- SQL Server
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
npm install
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
dotnet test --configuration Release
```

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
