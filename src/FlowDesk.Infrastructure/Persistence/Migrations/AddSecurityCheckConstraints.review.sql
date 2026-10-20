BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020175908_AddSecurityCheckConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_Role] CHECK ([Role] IN (''Employee'', ''Manager'', ''Admin''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020175908_AddSecurityCheckConstraints'
)
BEGIN
    EXEC(N'ALTER TABLE [ApprovalRequests] ADD CONSTRAINT [CK_ApprovalRequests_Status] CHECK ([Status] IN (1, 2, 3, 4))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020175908_AddSecurityCheckConstraints'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020175908_AddSecurityCheckConstraints', N'10.0.0');
END;

COMMIT;
GO

