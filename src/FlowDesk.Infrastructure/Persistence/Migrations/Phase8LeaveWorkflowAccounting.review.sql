BEGIN TRANSACTION;
DROP INDEX [IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_CreatedAtUtc] ON [LeaveBalanceLedgerEntries];

ALTER TABLE [LeaveRequestDetails] ADD [CancellationOfRequestId] int NULL;

ALTER TABLE [LeaveRequestDetails] ADD [ConfiguredLeaveTypeId] int NULL;

ALTER TABLE [LeaveBalanceLedgerEntries] ADD [EffectiveDate] date NULL;

UPDATE [LeaveBalanceLedgerEntries] SET [EffectiveDate] = CONVERT(date, [CreatedAtUtc]) WHERE [EffectiveDate] IS NULL;

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[LeaveBalanceLedgerEntries]') AND [c].[name] = N'EffectiveDate');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [LeaveBalanceLedgerEntries] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [LeaveBalanceLedgerEntries] ALTER COLUMN [EffectiveDate] date NOT NULL;

CREATE TABLE [LeaveRequestPolicyDays] (
    [RequestId] int NOT NULL,
    [LeaveDate] date NOT NULL,
    [LeavePolicyId] int NOT NULL,
    [Units] decimal(18,2) NOT NULL,
    [AllowsNegativeBalance] bit NOT NULL,
    CONSTRAINT [PK_LeaveRequestPolicyDays] PRIMARY KEY ([RequestId], [LeaveDate]),
    CONSTRAINT [CK_LeaveRequestPolicyDays_Units] CHECK ([Units] > 0),
    CONSTRAINT [FK_LeaveRequestPolicyDays_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id]),
    CONSTRAINT [FK_LeaveRequestPolicyDays_LeavePolicies_LeavePolicyId] FOREIGN KEY ([LeavePolicyId]) REFERENCES [LeavePolicies] ([Id])
);

CREATE UNIQUE INDEX [IX_LeaveRequestDetails_CancellationOfRequestId] ON [LeaveRequestDetails] ([CancellationOfRequestId]) WHERE [CancellationOfRequestId] IS NOT NULL;

CREATE INDEX [IX_LeaveRequestDetails_ConfiguredLeaveTypeId] ON [LeaveRequestDetails] ([ConfiguredLeaveTypeId]);

ALTER TABLE [LeaveRequestDetails] ADD CONSTRAINT [CK_LeaveRequestDetails_CancellationOfRequest] CHECK ([CancellationOfRequestId] IS NULL OR [CancellationOfRequestId] > 0);

CREATE INDEX [IX_LeaveBalanceLedgerEntries_EmployeeUserId_LeaveTypeId_EffectiveDate_CreatedAtUtc] ON [LeaveBalanceLedgerEntries] ([EmployeeUserId], [LeaveTypeId], [EffectiveDate], [CreatedAtUtc]);

CREATE INDEX [IX_LeaveRequestPolicyDays_LeavePolicyId] ON [LeaveRequestPolicyDays] ([LeavePolicyId]);

ALTER TABLE [LeaveRequestDetails] ADD CONSTRAINT [FK_LeaveRequestDetails_ApprovalRequests_CancellationOfRequestId] FOREIGN KEY ([CancellationOfRequestId]) REFERENCES [ApprovalRequests] ([Id]);

ALTER TABLE [LeaveRequestDetails] ADD CONSTRAINT [FK_LeaveRequestDetails_LeaveTypes_ConfiguredLeaveTypeId] FOREIGN KEY ([ConfiguredLeaveTypeId]) REFERENCES [LeaveTypes] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261023202154_Phase8LeaveWorkflowAccounting', N'10.0.0');

COMMIT;
GO

