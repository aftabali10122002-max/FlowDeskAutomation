BEGIN TRANSACTION;
ALTER TABLE [RequestAttachments] ADD [LastScanAttemptAtUtc] datetime2 NULL;

ALTER TABLE [RequestAttachments] ADD [NextScanAttemptAtUtc] datetime2 NULL;

ALTER TABLE [RequestAttachments] ADD [ScanAttempts] int NOT NULL DEFAULT 0;

ALTER TABLE [RequestAttachments] ADD [ScanFailureCode] nvarchar(40) NULL;

ALTER TABLE [RequestAttachments] ADD [ScanLeaseExpiresAtUtc] datetime2 NULL;

ALTER TABLE [RequestAttachments] ADD [ScanLeaseToken] uniqueidentifier NULL;

CREATE INDEX [IX_RequestAttachments_ScanStatus_NextScanAttemptAtUtc_ScanLeaseExpiresAtUtc_Id] ON [RequestAttachments] ([ScanStatus], [NextScanAttemptAtUtc], [ScanLeaseExpiresAtUtc], [Id]);

ALTER TABLE [RequestAttachments] ADD CONSTRAINT [CK_RequestAttachments_ScanAttempts] CHECK ([ScanAttempts] >= 0);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261027205056_Phase10DurableAttachmentScanning', N'10.0.0');

COMMIT;
GO

