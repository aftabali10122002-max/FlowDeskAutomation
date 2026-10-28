# FlowDesk Phase 10 handover

## Status

**PHASE 10 IMPLEMENTED — EXTERNAL DEPENDENCIES PENDING**

The attachment and approval-gate implementation is present and automated
checks passed in this worktree. This is not operational malware-scanning or
live-release approval: no ClamAV service or approved database target was
available for verification.

## Implementation

- Attachments are stored in private storage outside `wwwroot`; profile-image
  storage remains separate.
- Uploads enforce extension/signature validation, size/count limits,
  per-user concurrency limits, and idempotency.
- New attachments start `Pending`. Only `Clean` attachments can be previewed
  or downloaded; unconfigured, failed, rejected, and retry-exhausted scans
  remain inaccessible.
- The hosted scan worker atomically claims records under a serializable
  transaction, records attempts/timestamps and expiring lease tokens, checks
  stored SHA-256 before scanning, retries with bounded exponential backoff,
  and uses a conditional lease-token update to reject stale results.
- Both workflow-stage and legacy approval decisions check every active
  attachment inside the decision transaction. Upload and approval paths
  serialize against the request row so an upload cannot attach a file after
  approval. Requests without attachments retain their normal flow.
- Cleanup retries soft-deleted files, excludes files with live scan leases,
  and pages old storage candidates. It removes an orphan only after the grace
  period and a metadata-reference check; failures are retried. Logs omit
  filenames, storage keys, and content.
- The ClamAV adapter uses TCP `INSTREAM`, validates bounded scanner responses,
  observes a timeout, and verifies the streamed content hash. TCP ClamAV does
  not provide transport encryption or authentication.

The startup path registers the scan and cleanup workers but does not apply EF
migrations. `Attachments:AllowUnscannedDownloads=true` is rejected by
configuration validation and must not be enabled.

## Scanner setup

Current configuration evidence: the application configuration has no selected
provider or scanner private IP, the corresponding environment variables are
not set, and no scanner accepted a local connection on TCP port 3310. Therefore
**real malware scanning is not configured or verified**. The app remains
fail-closed; do not interpret a successful test fake as ClamAV verification.

Configure these values outside source control, in the API deployment
configuration store:

- `Attachments__Scanner__Provider=ClamAV`
- `Attachments__Scanner__PrivateIpAddress=<private-IP>`
- `Attachments__Scanner__Port=3310` (or the approved internal port)
- `Attachments__Scanner__TimeoutSeconds=30` (within the supported 1–120 range)
- Keep `Attachments__AllowUnscannedDownloads=false` or unset.

The IP must be a private address. Restrict its network ingress to the API
service with deployment firewall/security-group rules. Before accepting
attachments in the target environment, verify a known-clean test file and a
harmless standard antivirus test fixture (never a real malware sample), plus
timeout, unavailable-scanner, malformed-response, hash-mismatch, and
inaccessible-pending/rejected behavior. Use no confidential employee documents.
Record the scanner version/signature database freshness and the controlled
verification evidence in the deployment record.

Controlled scanner verification sequence:

1. Deploy ClamAV as a private service with current signatures. Restrict its TCP
   port to the API service; the TCP protocol is unauthenticated and unencrypted.
2. Set the environment variables above in the API deployment configuration,
   restart the API, and confirm startup accepts the provider and private IP.
   Confirm the API scan worker can reach the configured service using an
   isolated test request, not by exposing the service publicly.
3. Upload a non-sensitive known-clean test document. Confirm its status moves
   from `Pending` to `Clean` and an authorized user can download it.
4. Upload the harmless EICAR antivirus test file obtained from the official
   EICAR test-file source. Confirm the scanner rejects it and preview/download
   remains denied. Do not use real malware.
5. In an isolated test configuration, separately exercise timeout,
   unavailable-service, malformed-response, and content-hash-mismatch paths.
   Confirm each remains non-clean/inaccessible, retry scheduling and
   exhaustion behave as expected, and no failed scan is represented as
   `Clean`.
6. Record the ClamAV version, signature freshness, test outcomes, and
   deployment network-policy evidence. Remove test files through the
   application’s approved cleanup process.

See [`request-attachments.md`](./request-attachments.md) for all worker
configuration defaults and supported bounds.

## Migrations and database release checklist

Known migration chain in source order (the actual pending subset depends on
the approved target's migration history and has not been determined):

1. `20261019211631_ExistingSchemaBaseline`
2. `20261019212121_AddRequestOwnershipAndAssignments`
3. `20261020175908_AddSecurityCheckConstraints`
4. `20261020212545_AddApprovalWorkflowPhase4`
5. `20261020231021_AddWorkflowNotifications`
6. `20261021160706_AddRequestDescription`
7. `20261021185821_AddEmployeeSelfServicePortal`
8. `20261023173120_Phase8LeaveManagementFoundation`
9. `20261023194745_Phase8LeaveAccountingFoundation`
10. `20261023202154_Phase8LeaveWorkflowAccounting`
11. `20261023202717_Phase8ScheduledLeaveDate`
12. `20261026144202_Phase8PolicyScopeAndOverlapIndexes`
13. `20261027192600_Phase10RequestAttachments`
14. `20261027205056_Phase10DurableAttachmentScanning`
15. `20261028151134_Phase10AuditLogs`

The three Phase 10 migration files are under
`src/FlowDesk.Infrastructure/Persistence/Migrations/`; offline review scripts
are `Phase10RequestAttachments.review.sql`,
`Phase10DurableAttachmentScanning.review.sql`, and
`Phase10AuditLogs.review.sql`. Review scripts are not present for every
predecessor migration. Generate and review SQL for every migration pending on
the target before approval; do not assume the target starts at the Phase 10
boundary. `ExistingSchemaBaseline` creates `Users` and `ApprovalRequests`.
If those tables already exist without the matching EF history record, stop and
reconcile the target schema/history rather than applying the baseline blindly.

The attachment metadata migration adds foreign keys to requests, uploaders,
and deleters; unique client-upload and storage-key indexes; request, cleanup,
and uploader indexes; and deletion-audit and scan-status constraints. The
durable-scanning migration adds the attempt count, last/next attempt times,
failure code, lease token/expiry, a nonnegative-attempt constraint, and the
scan scheduling index. The checked-in EF snapshot contains both sets of
properties, indexes, and constraints; the offline EF check reported no model
changes since the last migration.

Neither migration nor review SQL was applied. No SQL Server connection was
made. Prior to a controlled release, an authorized DBA/operator must complete
all of the following:

1. **Verify target identity:** independently identify the approved server,
   database, environment, and migration history. Confirm the application
   connection configuration points to that exact target. Never use or access
   `FlowDeskDb_New`; do not infer authorization from a local connection string.
2. **Verify recovery:** take and restore-test a full database backup and
   separately back up the private attachment-storage directory. Confirm the
   recovery owner and maintenance window.
3. **Check migration prerequisites:** compare the live EF migration history
   and schema against the reviewed baseline and all predecessor migrations.
   Review every pending migration script; stop if unexpected migrations or
   schema differences exist.
4. **Preflight:** verify the target is reachable only through the approved
   release account and that the expected `Users`, `ApprovalRequests`, and EF
   history state match the migration prerequisites. Confirm the required
   disk/storage capacity and scanner network policy.
5. **Apply in order:** apply all approved predecessor migrations first, then
   `20261027192600_Phase10RequestAttachments`, then
   `20261027205056_Phase10DurableAttachmentScanning`. After separate explicit
   authorization and DBA review, the command for a deployment environment
   configured to the verified target is:

   ```powershell
   dotnet ef database update 20261028151134_Phase10AuditLogs --project src\FlowDesk.Infrastructure\FlowDesk.Infrastructure.csproj --startup-project src\FlowDisk.Api\FlowDisk.Api.csproj --configuration Release
   ```

   This command is documentation only and has **not** been run here. It
   applies any pending predecessors as well as all migrations through Audit
   Logs; do not execute it until the complete pending set is reviewed and
   approved.
6. **Verify schema:** inspect the resulting EF history, table/column types,
   nullability, foreign keys, indexes, and check constraints against both
   migrations and review SQL. Confirm startup did not trigger any extra
   migration.
7. **Rollback:** if only the durable-scanning migration was applied and no
   incompatible writes occurred, a reviewed rollback may remove its added
   columns/index/constraint. Rolling back the metadata migration drops
   `RequestAttachments` and its metadata; this is destructive after records
   exist. Prefer a forward repair or a verified database-and-storage restore
   over dropping that table. Rolling back `Phase10AuditLogs` drops
   `AuditLogEntries`, destroying the audit trail. Other migrations also remove
   tables, columns, or indexes in their `Down` methods; review each rollback
   against target data and the associated migration before executing it.
   Coordinate storage recovery because attachment files are outside the
   database.
8. **Smoke test:** using an approved isolated test request/account, verify
   pending files cannot be accessed, clean files can be accessed only by
   authorized users, non-clean files remain denied, the approval gate blocks
   non-clean active attachments, no-attachment approvals still work, removal
   and cleanup retry correctly, and profile-image upload/read/remove still
   work. Also verify the relevant create/assign/approve/reject, workflow-rule,
   and user role/status actions appear in Audit Logs for an Admin, and that
   non-Admin access is denied. Clean up only the test records through the
   approved test process.
9. **Post-deployment health:** verify API readiness and authentication,
   worker progress/retry behavior, storage capacity, scanner signature
   freshness, and cleanup health. Review logs/metrics for errors without
   exposing credentials, file names, storage keys, or file contents.

Do not execute the command or checklist without explicit database-access and
migration-application approval.

## Verification completed

- Complete backend suite: **191 passed, 2 skipped**. The skipped tests are
  SQL Server integration tests; the connection-string environment variable
  was explicitly unset for the run.
- Complete frontend suite: **61 passed**.
- Frontend lint: passed.
- TypeScript project checks and production Vite build: passed.
- Offline EF migration/model consistency: no pending model changes.
- `git diff --check`: passed (Git emitted line-ending conversion warnings for
  modified files).

The backend tests use isolated SQLite/test doubles and do not verify a live
SQL Server or ClamAV service. No live attachment end-to-end scenario was run;
it requires an approved test environment and configured scanner. The EF model
check is offline and does not establish which migrations are pending on any
database.

## Frontend regression investigation

The complete current frontend suite passes. The mobile drawer focus test
asserts both initial focus and restoration to the menu button, with a longer
test timeout to accommodate suite scheduling; the assertions are unchanged.
The Manager sign-in helper asserts the greeting, and the drawer navigation
test now also returns from Request History to Dashboard and verifies the
greeting renders again. The two previously reported failures were not
reproduced; the greeting regression scenario is covered by this assertion.

## Worktree note

The repository contains many modified and untracked files. Preserve them and
inspect the current Git status before release; this handover is not an
exhaustive list of the worktree diff.
