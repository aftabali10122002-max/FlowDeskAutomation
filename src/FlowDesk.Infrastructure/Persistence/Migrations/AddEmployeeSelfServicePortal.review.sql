BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021160706_AddRequestDescription'
)
BEGIN
    ALTER TABLE [ApprovalRequests] ADD [Description] nvarchar(2000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021160706_AddRequestDescription'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021160706_AddRequestDescription', N'10.0.0');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    ALTER TABLE [Users] ADD [PhoneNumber] nvarchar(32) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    ALTER TABLE [Users] ADD [ProfileImageStorageKey] nvarchar(128) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    ALTER TABLE [ApprovalWorkflowRules] ADD [RequestTypeId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ApprovalRequests]') AND [c].[name] = N'Amount');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [ApprovalRequests] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [ApprovalRequests] ALTER COLUMN [Amount] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    ALTER TABLE [ApprovalRequests] ADD [RequestTypeId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [ExpenseReimbursementRequestDetails] (
        [RequestId] int NOT NULL,
        [Category] nvarchar(100) NOT NULL,
        [ExpenseDate] date NOT NULL,
        [BusinessJustification] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_ExpenseReimbursementRequestDetails] PRIMARY KEY ([RequestId]),
        CONSTRAINT [FK_ExpenseReimbursementRequestDetails_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [ItAccessRequestDetails] (
        [RequestId] int NOT NULL,
        [SystemApplication] nvarchar(150) NOT NULL,
        [RequestedAccessLevel] nvarchar(100) NOT NULL,
        [DurationDays] int NULL,
        [BusinessJustification] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_ItAccessRequestDetails] PRIMARY KEY ([RequestId]),
        CONSTRAINT [CK_ItAccessRequestDetails_DurationDays] CHECK ([DurationDays] IS NULL OR [DurationDays] > 0),
        CONSTRAINT [FK_ItAccessRequestDetails_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [LeaveRequestDetails] (
        [RequestId] int NOT NULL,
        [LeaveType] nvarchar(50) NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NOT NULL,
        [Reason] nvarchar(2000) NOT NULL,
        [TotalLeaveDays] int NOT NULL,
        CONSTRAINT [PK_LeaveRequestDetails] PRIMARY KEY ([RequestId]),
        CONSTRAINT [CK_LeaveRequestDetails_DateRange] CHECK ([EndDate] >= [StartDate]),
        CONSTRAINT [CK_LeaveRequestDetails_TotalLeaveDays] CHECK ([TotalLeaveDays] > 0),
        CONSTRAINT [FK_LeaveRequestDetails_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [MaterialPurchaseRequestDetails] (
        [RequestId] int NOT NULL,
        [ItemName] nvarchar(200) NOT NULL,
        [Quantity] int NOT NULL,
        [BusinessJustification] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_MaterialPurchaseRequestDetails] PRIMARY KEY ([RequestId]),
        CONSTRAINT [CK_MaterialPurchaseRequestDetails_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_MaterialPurchaseRequestDetails_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [RequestTypes] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [DisplayName] nvarchar(100) NOT NULL,
        [RequiresAmount] bit NOT NULL,
        [IsEnabled] bit NOT NULL,
        CONSTRAINT [PK_RequestTypes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [TravelRequestDetails] (
        [RequestId] int NOT NULL,
        [Destination] nvarchar(200) NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NOT NULL,
        [BusinessPurpose] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_TravelRequestDetails] PRIMARY KEY ([RequestId]),
        CONSTRAINT [CK_TravelRequestDetails_DateRange] CHECK ([EndDate] >= [StartDate]),
        CONSTRAINT [FK_TravelRequestDetails_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE TABLE [WorkFromHomeRequestDetails] (
        [RequestId] int NOT NULL,
        [StartDate] date NOT NULL,
        [EndDate] date NOT NULL,
        [Reason] nvarchar(2000) NOT NULL,
        CONSTRAINT [PK_WorkFromHomeRequestDetails] PRIMARY KEY ([RequestId]),
        CONSTRAINT [CK_WorkFromHomeRequestDetails_DateRange] CHECK ([EndDate] >= [StartDate]),
        CONSTRAINT [FK_WorkFromHomeRequestDetails_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Code', N'DisplayName', N'IsEnabled', N'RequiresAmount') AND [object_id] = OBJECT_ID(N'[RequestTypes]'))
        SET IDENTITY_INSERT [RequestTypes] ON;
    EXEC(N'INSERT INTO [RequestTypes] ([Id], [Code], [DisplayName], [IsEnabled], [RequiresAmount])
    VALUES (1, N''MATERIAL_PURCHASE'', N''Material Purchase'', CAST(1 AS bit), CAST(1 AS bit)),
    (2, N''LEAVE'', N''Leave Request'', CAST(1 AS bit), CAST(0 AS bit)),
    (3, N''EXPENSE_REIMBURSEMENT'', N''Expense Reimbursement'', CAST(1 AS bit), CAST(1 AS bit)),
    (4, N''IT_ACCESS'', N''IT Access'', CAST(1 AS bit), CAST(0 AS bit)),
    (5, N''TRAVEL'', N''Travel Request'', CAST(1 AS bit), CAST(1 AS bit)),
    (6, N''WORK_FROM_HOME'', N''Work From Home'', CAST(1 AS bit), CAST(0 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Code', N'DisplayName', N'IsEnabled', N'RequiresAmount') AND [object_id] = OBJECT_ID(N'[RequestTypes]'))
        SET IDENTITY_INSERT [RequestTypes] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowRules_RequestTypeId] ON [ApprovalWorkflowRules] ([RequestTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE INDEX [IX_ApprovalRequests_RequestTypeId] ON [ApprovalRequests] ([RequestTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RequestTypes_Code] ON [RequestTypes] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    ALTER TABLE [ApprovalRequests] ADD CONSTRAINT [FK_ApprovalRequests_RequestTypes_RequestTypeId] FOREIGN KEY ([RequestTypeId]) REFERENCES [RequestTypes] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    ALTER TABLE [ApprovalWorkflowRules] ADD CONSTRAINT [FK_ApprovalWorkflowRules_RequestTypes_RequestTypeId] FOREIGN KEY ([RequestTypeId]) REFERENCES [RequestTypes] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261021185821_AddEmployeeSelfServicePortal'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261021185821_AddEmployeeSelfServicePortal', N'10.0.0');
END;

COMMIT;
GO

