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
Planned or in-progress capabilities:
- In-app notifications
- Email notifications
- Real-time updates using SignalR
- Deadline reminders
- Escalation alerts

Feature availability depends on the current implementation and validation status.

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

Phase 4 focuses on the advanced approval workflow. Phase 5 focuses on notifications, email integration, SignalR updates, deadline reminders, and escalation alerts.

Check the implementation and automated test results before treating individual features as production-ready.

## Contributing

1. Create a feature branch.
2. Make focused changes.
3. Add or update automated tests.
4. Run backend and frontend validation.
5. Submit a pull request describing the changes.

## License

No license has been specified yet. All rights are reserved unless a license is added to this repository.
