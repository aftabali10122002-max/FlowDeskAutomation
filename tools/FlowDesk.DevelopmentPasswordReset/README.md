# Development password reset

This is a separate, manual command for resetting explicitly selected accounts
in the local `FlowDeskDb_Phase4Test` database. It is not called by API startup
or by the development seeder. It does not apply migrations.

The command requires a Development environment and a SQL Server connection
string targeting `FlowDeskDb_Phase4Test` on `localhost`. It verifies the
connected database and local server before prompting for confirmation. Only
the selected users' password hashes are updated, using the application's
ASP.NET Core Identity password hasher. Selected accounts must all exist; if any
are missing, the transaction makes no changes. Roles, departments, requests,
assignments, and workflow history are not modified.

Run from the solution root in PowerShell. Enter the new password twice at the
hidden prompts; it is not echoed or passed as a command-line argument.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:FLOWDESK_DEV_PASSWORD_RESET_CONNECTION_STRING = "Server=localhost;Database=FlowDeskDb_Phase4Test;Trusted_Connection=True;TrustServerCertificate=True;"

try {
    dotnet run --project .\tools\FlowDesk.DevelopmentPasswordReset\FlowDesk.DevelopmentPasswordReset.csproj -- --reset-development-passwords --email "flowdesk.seed.v1.employee.it.01@dev.flowdesk.test" --email "flowdesk.seed.v1.manager.it.01@dev.flowdesk.test"
}
finally {
    Remove-Item Env:\FLOWDESK_DEV_PASSWORD_RESET_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
}
```

The command requires typing `RESET PASSWORDS IN FlowDeskDb_Phase4Test` before
it will prompt for the new password and update hashes. Do not run it until the
selected accounts and target database have been reviewed.
