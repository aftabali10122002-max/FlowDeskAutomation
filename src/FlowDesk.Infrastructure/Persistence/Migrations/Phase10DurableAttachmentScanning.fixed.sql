SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE [dbo].[RequestAttachments]
        ADD [LastScanAttemptAtUtc] datetime2 NULL;

    ALTER TABLE [dbo].[RequestAttachments]
        ADD [NextScanAttemptAtUtc] datetime2 NULL;

    ALTER TABLE [dbo].[RequestAttachments]
        ADD [ScanAttempts] int NOT NULL
        CONSTRAINT [DF_RequestAttachments_ScanAttempts] DEFAULT (0);

    ALTER TABLE [dbo].[RequestAttachments]
        ADD [ScanFailureCode] nvarchar(40) NULL;

    ALTER TABLE [dbo].[RequestAttachments]
        ADD [ScanLeaseExpiresAtUtc] datetime2 NULL;

    ALTER TABLE [dbo].[RequestAttachments]
        ADD [ScanLeaseToken] uniqueidentifier NULL;

    EXEC sys.sp_executesql N'
        CREATE INDEX [IX_RequestAttachments_ScanStatus_NextScanAttemptAtUtc_ScanLeaseExpiresAtUtc_Id]
        ON [dbo].[RequestAttachments]
        ([ScanStatus], [NextScanAttemptAtUtc], [ScanLeaseExpiresAtUtc], [Id]);';

    EXEC sys.sp_executesql N'
        ALTER TABLE [dbo].[RequestAttachments]
        ADD CONSTRAINT [CK_RequestAttachments_ScanAttempts]
        CHECK ([ScanAttempts] >= 0);';

    INSERT INTO [dbo].[__EFMigrationsHistory]
        ([MigrationId], [ProductVersion])
    VALUES
        (N'20261027205056_Phase10DurableAttachmentScanning', N'10.0.0');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
