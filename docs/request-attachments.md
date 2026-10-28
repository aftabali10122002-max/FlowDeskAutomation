# Request attachment scanning and cleanup

Request attachments are stored outside `wwwroot` and are unavailable until a
scanner records a clean result. The application does not apply EF migrations at
startup; deploy the generated migration through the approved database release
process before enabling the scan worker.

The scanner worker uses these optional configuration values:

| Setting | Default | Supported range |
|---|---:|---:|
| `Attachments:Scanner:IntervalSeconds` | 15 | 1–3600 |
| `Attachments:Scanner:BatchSize` | 10 | 1–100 |
| `Attachments:Scanner:MaximumAttempts` | 6 | 1–20 |
| `Attachments:Scanner:LeaseSeconds` | 180 | Scanner timeout + 30–600 |
| `Attachments:Scanner:RetryBaseSeconds` | 30 | 1–3600 |
| `Attachments:Scanner:RetryMaximumSeconds` | 3600 | Base delay–86400 |
| `Attachments:OrphanFileGraceHours` | 24 | 1–720 |
| `Attachments:CleanupIntervalSeconds` | 60 | 1–3600 |
| `Attachments:CleanupBatchSize` | 25 | 1–100 |

Retry delay doubles after each failed attempt and is capped by
`RetryMaximumSeconds`. Claims are persisted with a unique lease token and
expiry. Expired claims can be retried; results from an expired or superseded
lease are ignored. Exhausted or failed scans remain inaccessible.

Select the scanner explicitly using external configuration, for example
`Attachments__Scanner__Provider=ClamAV`, and supply the scanner's private IP
address, port, and timeout outside source control. The ClamAV TCP protocol has
no built-in transport encryption or authentication; restrict access to the
scanner endpoint to the API service. A configured provider is not evidence that
the deployment is operational: verify connectivity and test-file behavior in
the target environment before accepting uploads that require scanning.

Use these environment variables for the ClamAV provider:

| Environment variable | Purpose | Default |
|---|---|---:|
| `Attachments__Scanner__Provider` | Select `ClamAV` (otherwise scanning is explicitly unconfigured) | `Unconfigured` |
| `Attachments__Scanner__PrivateIpAddress` | Private IP of the scanner; DNS names and public IPs are rejected | Required for ClamAV |
| `Attachments__Scanner__Port` | ClamAV TCP port | 3310 |
| `Attachments__Scanner__TimeoutSeconds` | Maximum scan duration | 30 |
| `Attachments__AllowUnscannedDownloads` | Must remain false; startup rejects true | false |

Set configuration in the deployment secret/configuration store, not in source
control. Example names (with placeholders only):
`Attachments__Scanner__Provider=ClamAV`,
`Attachments__Scanner__PrivateIpAddress=<private-IP>`,
`Attachments__Scanner__Port=3310`, and
`Attachments__Scanner__TimeoutSeconds=30`. Restrict scanner network ingress to
the API service and validate the actual firewall/network policy independently.
Do not upload confidential employee documents to a public scanning service.

Orphan recovery examines private-storage entries in bounded, deterministic
pages and deletes only old entries whose storage keys have no attachment
metadata. Soft-deleted files with an active scan lease are left in place until
the lease expires. Temporary upload files and unrecognized filenames are not
enumerated as attachment objects.

The Phase 10 migration order is:

1. `20261027192600_Phase10RequestAttachments`
2. `20261027205056_Phase10DurableAttachmentScanning`

The matching `Phase10RequestAttachments.review.sql` and
`Phase10DurableAttachmentScanning.review.sql` scripts are for offline review
only. Application startup does not apply migrations. The first migration's
`Down` method drops the attachment metadata table; do not roll it back after
attachment records exist. The second migration can be rolled back separately,
but removes persisted retry and lease state.

Before any release, follow the database identity, backup, migration-order,
preflight, verification, and rollback checklist in
[`PROJECT_HANDOVER.md`](./PROJECT_HANDOVER.md). Migration application and live
database checks require separate explicit authorization.
