BEGIN TRANSACTION;
CREATE TABLE [RequestAttachments] (
    [Id] bigint NOT NULL IDENTITY,
    [RequestId] int NOT NULL,
    [UploadedByUserId] int NOT NULL,
    [StorageKey] uniqueidentifier NOT NULL,
    [ClientUploadId] uniqueidentifier NOT NULL,
    [OriginalFileName] nvarchar(255) NOT NULL,
    [Extension] nvarchar(10) NOT NULL,
    [MediaType] nvarchar(100) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [Sha256] char(64) NOT NULL,
    [UploadedAtUtc] datetime2 NOT NULL,
    [ScanStatus] int NOT NULL,
    [ScannedAtUtc] datetime2 NULL,
    [ScannerName] nvarchar(100) NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedByUserId] int NULL,
    [StorageCleanupPending] bit NOT NULL,
    CONSTRAINT [PK_RequestAttachments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_RequestAttachments_DeletionAudit] CHECK (([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL AND [StorageCleanupPending] = 0) OR ([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL)),
    CONSTRAINT [CK_RequestAttachments_ScanStatus] CHECK ([ScanStatus] IN (0, 1, 2, 3)),
    CONSTRAINT [FK_RequestAttachments_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id]),
    CONSTRAINT [FK_RequestAttachments_Users_DeletedByUserId] FOREIGN KEY ([DeletedByUserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_RequestAttachments_Users_UploadedByUserId] FOREIGN KEY ([UploadedByUserId]) REFERENCES [Users] ([Id])
);

CREATE UNIQUE INDEX [IX_RequestAttachments_ClientUploadId] ON [RequestAttachments] ([ClientUploadId]);

CREATE INDEX [IX_RequestAttachments_DeletedByUserId] ON [RequestAttachments] ([DeletedByUserId]);

CREATE INDEX [IX_RequestAttachments_RequestId_DeletedAtUtc_UploadedAtUtc] ON [RequestAttachments] ([RequestId], [DeletedAtUtc], [UploadedAtUtc]);

CREATE INDEX [IX_RequestAttachments_StorageCleanupPending_DeletedAtUtc] ON [RequestAttachments] ([StorageCleanupPending], [DeletedAtUtc]);

CREATE UNIQUE INDEX [IX_RequestAttachments_StorageKey] ON [RequestAttachments] ([StorageKey]);

CREATE INDEX [IX_RequestAttachments_UploadedByUserId] ON [RequestAttachments] ([UploadedByUserId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261027192600_Phase10RequestAttachments', N'10.0.0');

COMMIT;
GO

