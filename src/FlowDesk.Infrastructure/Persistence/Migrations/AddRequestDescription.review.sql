BEGIN TRANSACTION;
ALTER TABLE [ApprovalRequests] ADD [Description] nvarchar(2000) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261021160706_AddRequestDescription', N'10.0.0');

COMMIT;
GO

