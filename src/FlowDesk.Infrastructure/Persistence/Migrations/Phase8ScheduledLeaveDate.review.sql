BEGIN TRANSACTION;
ALTER TABLE [LeaveBalanceLedgerEntries] ADD [LeaveDate] date NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261023202717_Phase8ScheduledLeaveDate', N'10.0.0');

COMMIT;
GO

