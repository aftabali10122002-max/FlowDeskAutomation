BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    ALTER TABLE [Users] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    ALTER TABLE [ApprovalRequests] ADD [Priority] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    ALTER TABLE [ApprovalAssignments] ADD [WorkflowStageId] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [ApprovalWorkflowRules] (
        [Id] int NOT NULL IDENTITY,
        [WorkflowKey] nvarchar(100) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Version] int NOT NULL,
        [DepartmentId] int NULL,
        [MinimumAmount] decimal(18,2) NOT NULL,
        [MaximumAmountExclusive] decimal(18,2) NULL,
        [Priority] int NULL,
        [Status] int NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [PublishedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ApprovalWorkflowRules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalWorkflowRules_AmountRange] CHECK ([MinimumAmount] >= 0 AND ([MaximumAmountExclusive] IS NULL OR [MaximumAmountExclusive] > [MinimumAmount])),
        CONSTRAINT [CK_ApprovalWorkflowRules_Priority] CHECK ([Priority] IS NULL OR [Priority] IN (1, 2, 3, 4)),
        CONSTRAINT [CK_ApprovalWorkflowRules_Status] CHECK ([Status] IN (1, 2, 3)),
        CONSTRAINT [FK_ApprovalWorkflowRules_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [ApprovalWorkflowInstances] (
        [Id] bigint NOT NULL IDENTITY,
        [RequestId] int NOT NULL,
        [WorkflowRuleId] int NOT NULL,
        [RuleVersion] int NOT NULL,
        [Status] int NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ApprovalWorkflowInstances] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalWorkflowInstances_Status] CHECK ([Status] IN (1, 2, 3, 4)),
        CONSTRAINT [FK_ApprovalWorkflowInstances_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id]),
        CONSTRAINT [FK_ApprovalWorkflowInstances_ApprovalWorkflowRules_WorkflowRuleId] FOREIGN KEY ([WorkflowRuleId]) REFERENCES [ApprovalWorkflowRules] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [ApprovalWorkflowRuleStages] (
        [Id] int NOT NULL IDENTITY,
        [WorkflowRuleId] int NOT NULL,
        [Sequence] int NOT NULL,
        [ApproverType] int NOT NULL,
        [ApproverDepartmentId] int NULL,
        [SpecificApproverUserId] int NULL,
        [DeadlineMinutes] int NULL,
        [EscalationAfterMinutes] int NULL,
        [EscalationApproverUserId] int NULL,
        CONSTRAINT [PK_ApprovalWorkflowRuleStages] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalWorkflowRuleStages_ApproverType] CHECK ([ApproverType] IN (1, 2, 3)),
        CONSTRAINT [CK_ApprovalWorkflowRuleStages_Deadline] CHECK ([DeadlineMinutes] IS NULL OR [DeadlineMinutes] > 0),
        CONSTRAINT [CK_ApprovalWorkflowRuleStages_Escalation] CHECK ([EscalationAfterMinutes] IS NULL OR ([EscalationAfterMinutes] >= [DeadlineMinutes] AND [DeadlineMinutes] IS NOT NULL AND [EscalationApproverUserId] IS NOT NULL)),
        CONSTRAINT [CK_ApprovalWorkflowRuleStages_Sequence] CHECK ([Sequence] > 0),
        CONSTRAINT [FK_ApprovalWorkflowRuleStages_ApprovalWorkflowRules_WorkflowRuleId] FOREIGN KEY ([WorkflowRuleId]) REFERENCES [ApprovalWorkflowRules] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ApprovalWorkflowRuleStages_Departments_ApproverDepartmentId] FOREIGN KEY ([ApproverDepartmentId]) REFERENCES [Departments] ([Id]),
        CONSTRAINT [FK_ApprovalWorkflowRuleStages_Users_EscalationApproverUserId] FOREIGN KEY ([EscalationApproverUserId]) REFERENCES [Users] ([Id]),
        CONSTRAINT [FK_ApprovalWorkflowRuleStages_Users_SpecificApproverUserId] FOREIGN KEY ([SpecificApproverUserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [ApprovalWorkflowStages] (
        [Id] bigint NOT NULL IDENTITY,
        [WorkflowInstanceId] bigint NOT NULL,
        [Sequence] int NOT NULL,
        [Status] int NOT NULL,
        [ApproverUserId] int NOT NULL,
        [DeadlineMinutes] int NULL,
        [EscalationAfterMinutes] int NULL,
        [EscalationApproverUserId] int NULL,
        [ActivatedAtUtc] datetime2 NULL,
        [DueAtUtc] datetime2 NULL,
        [DecidedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_ApprovalWorkflowStages] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalWorkflowStages_Sequence] CHECK ([Sequence] > 0),
        CONSTRAINT [CK_ApprovalWorkflowStages_Status] CHECK ([Status] IN (1, 2, 3, 4, 5)),
        CONSTRAINT [FK_ApprovalWorkflowStages_ApprovalWorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [ApprovalWorkflowInstances] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ApprovalWorkflowStages_Users_ApproverUserId] FOREIGN KEY ([ApproverUserId]) REFERENCES [Users] ([Id]),
        CONSTRAINT [FK_ApprovalWorkflowStages_Users_EscalationApproverUserId] FOREIGN KEY ([EscalationApproverUserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [ApprovalDecisionHistory] (
        [Id] bigint NOT NULL IDENTITY,
        [WorkflowStageId] bigint NOT NULL,
        [ManagerUserId] int NOT NULL,
        [Decision] int NOT NULL,
        [Comment] nvarchar(2000) NULL,
        [DecidedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ApprovalDecisionHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ApprovalDecisionHistory_Decision] CHECK ([Decision] IN (3, 4)),
        CONSTRAINT [FK_ApprovalDecisionHistory_ApprovalWorkflowStages_WorkflowStageId] FOREIGN KEY ([WorkflowStageId]) REFERENCES [ApprovalWorkflowStages] ([Id]),
        CONSTRAINT [FK_ApprovalDecisionHistory_Users_ManagerUserId] FOREIGN KEY ([ManagerUserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [ApprovalWorkflowEscalations] (
        [Id] bigint NOT NULL IDENTITY,
        [WorkflowStageId] bigint NOT NULL,
        [EscalationNumber] int NOT NULL,
        [RecipientUserId] int NOT NULL,
        [EscalatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ApprovalWorkflowEscalations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ApprovalWorkflowEscalations_ApprovalWorkflowStages_WorkflowStageId] FOREIGN KEY ([WorkflowStageId]) REFERENCES [ApprovalWorkflowStages] ([Id]),
        CONSTRAINT [FK_ApprovalWorkflowEscalations_Users_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE TABLE [WorkflowNotifications] (
        [Id] bigint NOT NULL IDENTITY,
        [RecipientUserId] int NOT NULL,
        [EscalationId] bigint NULL,
        [Message] nvarchar(500) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ReadAtUtc] datetime2 NULL,
        CONSTRAINT [PK_WorkflowNotifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowNotifications_ApprovalWorkflowEscalations_EscalationId] FOREIGN KEY ([EscalationId]) REFERENCES [ApprovalWorkflowEscalations] ([Id]),
        CONSTRAINT [FK_WorkflowNotifications_Users_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    EXEC(N'ALTER TABLE [ApprovalRequests] ADD CONSTRAINT [CK_ApprovalRequests_Priority] CHECK ([Priority] IS NULL OR [Priority] IN (1, 2, 3, 4))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ApprovalAssignments_WorkflowStageId] ON [ApprovalAssignments] ([WorkflowStageId]) WHERE [WorkflowStageId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalDecisionHistory_ManagerUserId] ON [ApprovalDecisionHistory] ([ManagerUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApprovalDecisionHistory_WorkflowStageId] ON [ApprovalDecisionHistory] ([WorkflowStageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowEscalations_RecipientUserId] ON [ApprovalWorkflowEscalations] ([RecipientUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApprovalWorkflowEscalations_WorkflowStageId_EscalationNumber] ON [ApprovalWorkflowEscalations] ([WorkflowStageId], [EscalationNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApprovalWorkflowInstances_RequestId] ON [ApprovalWorkflowInstances] ([RequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowInstances_WorkflowRuleId_RuleVersion] ON [ApprovalWorkflowInstances] ([WorkflowRuleId], [RuleVersion]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowRules_DepartmentId] ON [ApprovalWorkflowRules] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowRules_Status_DepartmentId_MinimumAmount_MaximumAmountExclusive_Priority] ON [ApprovalWorkflowRules] ([Status], [DepartmentId], [MinimumAmount], [MaximumAmountExclusive], [Priority]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApprovalWorkflowRules_WorkflowKey_Version] ON [ApprovalWorkflowRules] ([WorkflowKey], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowRuleStages_ApproverDepartmentId] ON [ApprovalWorkflowRuleStages] ([ApproverDepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowRuleStages_EscalationApproverUserId] ON [ApprovalWorkflowRuleStages] ([EscalationApproverUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowRuleStages_SpecificApproverUserId] ON [ApprovalWorkflowRuleStages] ([SpecificApproverUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApprovalWorkflowRuleStages_WorkflowRuleId_Sequence] ON [ApprovalWorkflowRuleStages] ([WorkflowRuleId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowStages_ApproverUserId] ON [ApprovalWorkflowStages] ([ApproverUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowStages_EscalationApproverUserId] ON [ApprovalWorkflowStages] ([EscalationApproverUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_ApprovalWorkflowStages_Status_DueAtUtc] ON [ApprovalWorkflowStages] ([Status], [DueAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ApprovalWorkflowStages_WorkflowInstanceId] ON [ApprovalWorkflowStages] ([WorkflowInstanceId]) WHERE [Status] = 2');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApprovalWorkflowStages_WorkflowInstanceId_Sequence] ON [ApprovalWorkflowStages] ([WorkflowInstanceId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_WorkflowNotifications_EscalationId] ON [WorkflowNotifications] ([EscalationId]) WHERE [EscalationId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    CREATE INDEX [IX_WorkflowNotifications_RecipientUserId] ON [WorkflowNotifications] ([RecipientUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    ALTER TABLE [ApprovalAssignments] ADD CONSTRAINT [FK_ApprovalAssignments_ApprovalWorkflowStages_WorkflowStageId] FOREIGN KEY ([WorkflowStageId]) REFERENCES [ApprovalWorkflowStages] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020212545_AddApprovalWorkflowPhase4'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020212545_AddApprovalWorkflowPhase4', N'10.0.0');
END;

COMMIT;
GO

