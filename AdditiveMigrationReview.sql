BEGIN TRANSACTION;
ALTER TABLE [Users] ADD [DepartmentId] int NULL;

ALTER TABLE [ApprovalRequests] ADD [CreatedByUserId] int NULL;

ALTER TABLE [ApprovalRequests] ADD [DepartmentId] int NULL;

CREATE TABLE [ApprovalAssignments] (
    [Id] bigint NOT NULL IDENTITY,
    [RequestId] int NOT NULL,
    [ApproverUserId] int NOT NULL,
    [Status] int NOT NULL,
    [AssignedAtUtc] datetime2 NOT NULL,
    [DecidedAtUtc] datetime2 NULL,
    CONSTRAINT [PK_ApprovalAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_ApprovalAssignments_DecisionTime] CHECK (([Status] = 1 AND [DecidedAtUtc] IS NULL) OR ([Status] IN (2, 3, 4) AND [DecidedAtUtc] IS NOT NULL)),
    CONSTRAINT [CK_ApprovalAssignments_Status] CHECK ([Status] IN (1, 2, 3, 4)),
    CONSTRAINT [FK_ApprovalAssignments_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id]),
    CONSTRAINT [FK_ApprovalAssignments_Users_ApproverUserId] FOREIGN KEY ([ApproverUserId]) REFERENCES [Users] ([Id])
);

CREATE TABLE [Departments] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [Code] nvarchar(20) NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
);

CREATE INDEX [IX_Users_DepartmentId] ON [Users] ([DepartmentId]);

CREATE INDEX [IX_ApprovalRequests_CreatedByUserId_CreatedAtUtc] ON [ApprovalRequests] ([CreatedByUserId], [CreatedAtUtc]);

CREATE INDEX [IX_ApprovalRequests_DepartmentId_Status_CreatedAtUtc] ON [ApprovalRequests] ([DepartmentId], [Status], [CreatedAtUtc]);

CREATE INDEX [IX_ApprovalRequests_Status_CreatedAtUtc] ON [ApprovalRequests] ([Status], [CreatedAtUtc]);

CREATE INDEX [IX_ApprovalAssignments_ApproverUserId_Status_AssignedAtUtc] ON [ApprovalAssignments] ([ApproverUserId], [Status], [AssignedAtUtc]);

CREATE UNIQUE INDEX [IX_ApprovalAssignments_RequestId] ON [ApprovalAssignments] ([RequestId]) WHERE [Status] = 1;

CREATE INDEX [IX_ApprovalAssignments_RequestId_AssignedAtUtc] ON [ApprovalAssignments] ([RequestId], [AssignedAtUtc]);

CREATE UNIQUE INDEX [IX_Departments_Code] ON [Departments] ([Code]) WHERE [Code] IS NOT NULL;

CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);

ALTER TABLE [ApprovalRequests] ADD CONSTRAINT [FK_ApprovalRequests_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]);

ALTER TABLE [ApprovalRequests] ADD CONSTRAINT [FK_ApprovalRequests_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]);

ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261019212121_AddRequestOwnershipAndAssignments', N'10.0.0');

COMMIT;
GO

