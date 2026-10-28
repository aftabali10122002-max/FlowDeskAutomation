# FlowDesk Phase 10 remaining work

Phase 10 attachment code and the Audit Logs implementation are present.
Automated checks pass, but database deployment, private scanner verification,
and isolated live end-to-end checks remain external release gates. No database
or production scanner was accessed for this handover.

## Required before accepting scanned attachments

- Provision an approved private ClamAV service and a private API-to-scanner
  network path. Restrict access to the API service.
- Configure the ClamAV provider, private IP, port, and timeout outside source
  control. Keep `Attachments:AllowUnscannedDownloads` disabled.
- Verify scanner version/signature updates and run controlled clean, harmless
  infected-fixture, timeout, unavailable, malformed-response, and hash
  consistency checks in an isolated environment. Confirm all failures remain
  inaccessible.
- Monitor scan queue age, failure/retry exhaustion, cleanup retries, and disk
  capacity using operational telemetry that does not expose file contents or
  names.

## Required before database deployment

- Obtain explicit authorization for the exact target database and migration
  application.
- Have the DBA verify target identity and migration history, review all pending
  predecessor and Phase 10 scripts, and confirm backup and restore readiness
  for both database and private file storage.
- Apply the attachment metadata migration before the durable-scanning
  migration, then apply the Audit Logs migration after reviewing its script.
  Apply any other pending predecessor migrations first, in source order.
- Do not access `FlowDeskDb_New`. Do not apply a migration or connect to a
  database based only on this handover.
- The source chain begins with `20261019211631_ExistingSchemaBaseline`, which
  creates `Users` and `ApprovalRequests`. If those tables exist but its EF
  history entry does not, stop for DBA reconciliation; do not apply it blindly.
- `Phase10RequestAttachments.Down` drops the `RequestAttachments` table and
  its records. `Phase10AuditLogs.Down` drops the audit history. Review all
  rollback scripts before any rollback; prefer a forward repair or verified
  restore where data would be lost.

The command, checks, and rollback cautions are documented in
[`PROJECT_HANDOVER.md`](./PROJECT_HANDOVER.md). The metadata migration's
rollback drops the attachment table; do not use that rollback after records
exist.

## Required live verification

In an approved isolated environment, verify upload validation and limits,
authorization boundaries, pending/clean/rejected download behavior, Manager
approval blocking, normal approval for requests with no attachments, cleanup
retry/idempotency, and existing profile-image behavior. Do not create or alter
business data without authorization. No live end-to-end or real-scanner result
is claimed yet.

## Current automated evidence

- Backend suite: 191 passed, 2 SQL Server tests skipped.
- Frontend suite: 61 passed.
- Frontend lint, TypeScript/project build, production build, offline EF model
  check, and `git diff --check`: passed.
- The EF check confirms no model changes since the latest migration; it does
  not reveal the migration history or pending set on a deployment database.
- Current configuration has no selected ClamAV provider or scanner IP, and
  unscanned downloads remain disabled.
- Tests use local test infrastructure and do not constitute live SQL Server,
  ClamAV, or end-to-end verification.

Frontend drawer focus and Manager greeting failures were not reproduced in the
passing suite. Greeting is asserted at Manager sign-in and after returning
from Request History to Dashboard through the mobile drawer.
