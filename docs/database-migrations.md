# FlowDesk EF Core migration and existing-database baseline

The migration set has four ordered steps:

1. `ExistingSchemaBaseline` represents the existing `Users` and
   `ApprovalRequests` schema. Its `Up` method creates those tables for a new,
   empty database.
2. `AddRequestOwnershipAndAssignments` adds `Departments`, nullable department
   and request-owner columns, foreign keys and indexes, and
   `ApprovalAssignments`. It does not recreate or copy existing business rows.
3. `AddSecurityCheckConstraints` adds a CHECK constraint allowing only
   `Employee`, `Manager`, and `Admin` in `Users.Role`, and a CHECK constraint
   allowing request status values 1 through 4 in `ApprovalRequests.Status`.
   It changes no business rows. SQL Server validates existing rows when these
   constraints are added, so an unexpected value will cause the migration to
   fail rather than being rewritten.
4. `AddApprovalWorkflowPhase4` adds nullable request priority and workflow
   assignment fields, defaults existing users to active, and creates the
   versioned workflow, stage, decision-history, escalation, and notification
   tables with their foreign keys, indexes, and CHECK constraints. It does not
   backfill or reassign existing requests; requests without a matching active
   rule continue through the manual-assignment workflow.

## New database

For a genuinely empty database, apply the migrations in order using the normal
EF Core migration process. Review the generated SQL first. The baseline's
`Down` method drops the original business tables, so do not roll back the
baseline on a database containing business data.

## Existing database with no migration history

The existing FlowDesk database already has `Users` and `ApprovalRequests`, but
no `__EFMigrationsHistory` table. Do not run `database update` against it while
the baseline migration is unrecorded: EF would try to create those existing
tables again.

Before baselining an existing database:

1. Take and verify a restorable backup.
2. Compare the live schema against `ExistingSchemaBaseline`, including column
   types/nullability, identity primary keys, and the unique `Users.Email`
   index. Compare index semantics rather than relying on SQL Server's generated
   constraint/index names.
3. Have a DBA review and approve a baseline-only procedure that creates the EF
   migration-history table if needed and records the
   `ExistingSchemaBaseline` migration ID without executing its `CreateTable`
   operations. Do not insert guessed request ownership, departments, or
   assignments.
4. Review the additive migration SQL separately. Only after a separate
   deployment approval should the additive migration be applied.

The `AddSecurityCheckConstraints` and `AddApprovalWorkflowPhase4` migrations
and their separate SQL scripts were generated for review only. Neither
migration has been applied as part of this implementation. The Phase 4 script
assumes the security migration has already been applied; deploy migrations in
their defined order and only after separate approval. No baseline SQL or
migration-history rows have been generated or applied here.

## Read-only SQL Server preflight

Before considering a migration against an existing database, an authorized
developer or DBA can run [`database-preflight.sql`](./database-preflight.sql)
in SSMS using an explicitly approved read-only account and `FlowDeskDb_New` selected
as the current database. The script reads SQL Server catalog metadata and
returns aggregate counts for potential schema-compatibility issues, without
returning user or request details.

The script uses guarded dynamic SQL only for read queries whose tables or
columns may not exist yet. A `NOT RUN` result means the object was missing or
not visible to that account; it does not prove the absence of that object.
It reports aggregate counts for invalid role and request-status values before
the new CHECK constraints are considered. Database readiness remains
unconfirmed until a DBA reviews the actual results against all migration
definitions. The preflight does not register migration history, generate or
execute migration SQL, or make the database ready for migration by itself.

## Phase 3 security behavior

Login and registration use separate fixed-window limits configured by
`RateLimiting:Login:PermitLimit` and
`RateLimiting:Registration:PermitLimit` in `appsettings.json`. The local
defaults are 10 login attempts and 5 registrations per minute per remote IP;
rejected requests return HTTP 429 and a `Retry-After` header.

These counters are in-process and IP-based. A production deployment should
configure trusted forwarded headers for known proxies and enforce coordinated
limits at a gateway or shared/distributed limiter when running multiple API
instances. Consider account-aware limits and operational monitoring, while
accounting for shared NAT addresses. Do not trust arbitrary client-supplied
forwarded-IP headers.

JWT role claims are not revoked when a role changes. Privileged application
operations re-check the user's persisted role and reject stale Admin or
Manager tokens; claim-display endpoints such as `/api/auth/me` can still show
the old role until the token expires. A shared revocation/version mechanism
would be a separate change if immediate claim-wide invalidation is required.

## Phase 4 approval workflows

Admins configure draft workflow rules through `/api/admin/workflows`, then
explicitly activate a version. Rules may be scoped by department, priority,
and half-open amount range. A request is routed only when exactly one active
rule matches; if none matches, the existing manual assignment behavior is
preserved. Existing requests are not backfilled.

Workflow stages are sequential. Only the Manager assigned to the active stage
can decide it. Approval activates the next stage and creates its assignment;
the request becomes Approved only after the final stage. Rejection requires a
reason, which is stored in decision history. Employees may cancel their own
pending workflow requests, which closes the active assignment and remaining
stages.

Stages may have deadlines and a configured escalation recipient. Escalation
processing is opt-in through `POST /api/admin/workflows/process-escalations`;
there is no background worker. The operation persists an escalation and an
in-app notification, and repeated processing does not duplicate the same
stage escalation. Authenticated users can read and mark only their own
notifications through `/api/notifications`.

## Opt-in SQL Server integration tests

The SQL Server tests are skipped unless
`FLOWDESK_SQLSERVER_TEST_CONNECTION_STRING` is set. They require an already
provisioned, isolated database whose name starts with `FlowDesk_Test_`; the
test harness refuses `FlowDeskDb_New`, does not create a database, and does
not drop or reset one. When explicitly run, the suite applies pending EF
migrations to that test database, checks CHECK-constraint rejection using
rolled-back transactions, and exercises manager/admin authorization. Unique
synthetic authorization-test rows remain in that dedicated database; use a
disposable isolated test instance rather than production or development data.

## Initial administrator and assignment provisioning

Public registration continues to create Employee accounts only. There is no
public Admin bootstrap endpoint. The first Admin must be selected by an
authorized operator after verifying the account out of band; an approved,
one-time DBA operation can promote that specific existing user in a
transaction, with a backup and an external change/audit record. Do not put a
bootstrap credential or reusable promotion script in application settings or
source control. The user must log in again to receive a JWT with the current
role claim.

Manager roles also cannot be assigned through public registration. An Admin
can create Employee or Manager accounts, change roles and departments, and
activate or deactivate users through the protected user-management API.
Managers with open approval work and the last Admin account cannot be
deactivated or demoted.

An Admin can still manually assign a Manager to an owned, pending request
through the protected assignment endpoint when no active workflow rule matches.
The application verifies the target account's persisted active Manager role
and department. Existing requests with a null owner are not assignable through
that operation; they remain read-only legacy records until a separate,
explicitly approved and audited resolution workflow exists.

`ApprovalAssignments` remains the request-to-Manager assignment record.
`ApprovalDecisionHistory` stores each workflow-stage decision, optional
approval comments, and required rejection reasons. Legacy manual assignments
continue using their existing status and decision-time fields.
