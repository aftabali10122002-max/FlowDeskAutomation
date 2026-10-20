# FlowDesk development data seeder

This tool adds the versioned development dataset to an existing local database.
It is never invoked by API startup and does not apply migrations.

## Preconditions

- The SQL Server database already exists and has the current FlowDesk schema.
- The target is exactly `FlowDeskDb_New` on `localhost`.
- The process environment is `Development`.
- The four existing departments are present and active.
- Choose a development password of at least 12 characters. The same supplied
  password is hashed independently for all generated accounts using the
  application's ASP.NET Core Identity password hasher. The password is not
  printed or saved by the seeder.

## Manual execution (PowerShell)

Run from the solution root. Set the connection string through an environment
variable rather than editing source or configuration files. This example uses
Windows integrated authentication:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:FLOWDESK_DEV_SEED_CONNECTION_STRING = "Server=localhost;Database=FlowDeskDb_New;Trusted_Connection=True;TrustServerCertificate=True;"

$securePassword = Read-Host "Development seed password (12+ characters)" -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $env:FLOWDESK_DEV_SEED_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    dotnet run --project .\tools\FlowDesk.DevelopmentSeeder\FlowDesk.DevelopmentSeeder.csproj -- --seed-development-data
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    Remove-Item Env:\FLOWDESK_DEV_SEED_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\FLOWDESK_DEV_SEED_CONNECTION_STRING -ErrorAction SilentlyContinue
    Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    Remove-Item Env:\DOTNET_ENVIRONMENT -ErrorAction SilentlyContinue
}
```

Do not execute this against production or a shared database. The tool refuses
non-Development environments, non-local SQL Server targets, and any database
other than `FlowDeskDb_New`; it also verifies `DB_NAME()` after connecting.
The argument `--seed-development-data` is mandatory. No `EnsureCreated`,
migration, delete, truncate, reset, or schema-change operation is used.

The deterministic login accounts use addresses in the
`flowdesk.seed.v1.*@dev.flowdesk.test` namespace. The seed password is the
value entered through `FLOWDESK_DEV_SEED_PASSWORD`; the tool lists the account
addresses without logging the password. All generated users are Employees or
Managers, never Admins.

## Data allocation

| Department | Employees | Managers | Requests | Pending | Approved | Rejected | Cancelled |
|---|---:|---:|---:|---:|---:|---:|---:|
| IT | 5 | 2 | 30 | 9 | 12 | 6 | 3 |
| HR | 4 | 1 | 20 | 6 | 8 | 4 | 2 |
| Finance | 5 | 2 | 30 | 9 | 12 | 6 | 3 |
| Marketing | 4 | 1 | 20 | 6 | 8 | 4 | 2 |
| **Total** | **18** | **6** | **100** | **30** | **40** | **20** | **10** |

Each request has one same-department Employee owner and one same-department
Manager assignment. Pending request/assignment rows remain pending and have no
decision timestamp. Approved, Rejected, and Cancelled assignments have the
matching status and a UTC decision time after assignment. Cancellation follows
the existing request cancellation state transition and assignment status.

## Idempotency and failure behavior

Account email addresses and request titles are deterministic seed identifiers.
The seeder creates
all data in one serializable transaction, verifies totals, department/role
relationships, status consistency, and timestamp ordering before commit. A
complete existing seed is validated and treated as a no-op. Any partial or
conflicting seed is rejected without attempting to repair or overwrite it.
Database failures or failed validation roll back the transaction.

## Read-only SQL verification

These queries only read data. Seeded requests are identified by their
deterministic Employee email owners.

```sql
-- Users by role and department (expect 18 Employees and 6 Managers).
SELECT u.Role, d.Name AS DepartmentName, COUNT(*) AS UserCount
FROM dbo.Users AS u
JOIN dbo.Departments AS d ON d.Id = u.DepartmentId
WHERE u.Email LIKE 'flowdesk.seed.v1.%@dev.flowdesk.test'
GROUP BY u.Role, d.Name
ORDER BY d.Name, u.Role;

-- Requests by department and status (expect the allocation table above).
SELECT d.Name AS DepartmentName, r.Status, COUNT(*) AS RequestCount
FROM dbo.ApprovalRequests AS r
JOIN dbo.Departments AS d ON d.Id = r.DepartmentId
JOIN dbo.Users AS e ON e.Id = r.CreatedByUserId
WHERE e.Email LIKE 'flowdesk.seed.v1.employee.%@dev.flowdesk.test'
GROUP BY d.Name, r.Status
ORDER BY d.Name, r.Status;

-- Total request statuses: 1=Pending, 2=Approved, 3=Rejected, 4=Cancelled.
SELECT r.Status, COUNT(*) AS RequestCount
FROM dbo.ApprovalRequests AS r
JOIN dbo.Users AS e ON e.Id = r.CreatedByUserId
WHERE e.Email LIKE 'flowdesk.seed.v1.employee.%@dev.flowdesk.test'
GROUP BY r.Status
ORDER BY r.Status;

-- Must return no rows: every seeded request has exactly one valid,
-- same-department assignment whose status and decision time match.
SELECT r.Id
FROM dbo.ApprovalRequests AS r
JOIN dbo.Users AS e ON e.Id = r.CreatedByUserId
JOIN dbo.Departments AS d ON d.Id = r.DepartmentId
LEFT JOIN dbo.ApprovalAssignments AS a ON a.RequestId = r.Id
LEFT JOIN dbo.Users AS m ON m.Id = a.ApproverUserId
WHERE e.Email LIKE 'flowdesk.seed.v1.employee.%@dev.flowdesk.test'
GROUP BY r.Id, r.Status, r.DepartmentId
HAVING COUNT(a.Id) <> 1
    OR MIN(CASE WHEN e.Role = 'Employee'
                    AND e.DepartmentId = r.DepartmentId
                    AND m.Role = 'Manager'
                    AND m.DepartmentId = r.DepartmentId
                    AND r.CreatedAtUtc <= a.AssignedAtUtc
                    AND a.Status = r.Status
                    AND ((r.Status = 1 AND a.DecidedAtUtc IS NULL)
                         OR (r.Status IN (2, 3, 4)
                             AND a.DecidedAtUtc IS NOT NULL
                             AND a.DecidedAtUtc >= a.AssignedAtUtc))
                THEN 1 ELSE 0 END) = 0;

-- Must return no rows: no duplicate seed login identities.
SELECT Email, COUNT(*) AS DuplicateCount
FROM dbo.Users
WHERE Email LIKE 'flowdesk.seed.v1.%@dev.flowdesk.test'
GROUP BY Email
HAVING COUNT(*) > 1;

-- Must return no rows: deterministic seeded request titles are unique.
SELECT r.Title, COUNT(*) AS DuplicateCount
FROM dbo.ApprovalRequests AS r
JOIN dbo.Users AS e ON e.Id = r.CreatedByUserId
WHERE e.Email LIKE 'flowdesk.seed.v1.employee.%@dev.flowdesk.test'
GROUP BY r.Title
HAVING COUNT(*) > 1;
```
