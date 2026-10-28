BEGIN TRANSACTION;
ALTER TABLE [WorkflowNotifications] ADD [DeduplicationKey] nvarchar(160) NULL;

ALTER TABLE [WorkflowNotifications] ADD [RequestId] int NULL;

ALTER TABLE [WorkflowNotifications] ADD [Type] nvarchar(40) NOT NULL DEFAULT N'Escalation';

CREATE TABLE [WorkflowEmailOutbox] (
    [Id] bigint NOT NULL IDENTITY,
    [RecipientUserId] int NOT NULL,
    [NotificationId] bigint NOT NULL,
    [RecipientEmail] nvarchar(150) NOT NULL,
    [Subject] nvarchar(200) NOT NULL,
    [Body] nvarchar(4000) NOT NULL,
    [DeduplicationKey] nvarchar(160) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [SentAtUtc] datetime2 NULL,
    [LastAttemptAtUtc] datetime2 NULL,
    [LeaseUntilUtc] datetime2 NULL,
    [AttemptCount] int NOT NULL,
    CONSTRAINT [PK_WorkflowEmailOutbox] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_WorkflowEmailOutbox_Users_RecipientUserId] FOREIGN KEY ([RecipientUserId]) REFERENCES [Users] ([Id]),
    CONSTRAINT [FK_WorkflowEmailOutbox_WorkflowNotifications_NotificationId] FOREIGN KEY ([NotificationId]) REFERENCES [WorkflowNotifications] ([Id])
);

CREATE UNIQUE INDEX [IX_WorkflowNotifications_DeduplicationKey] ON [WorkflowNotifications] ([DeduplicationKey]) WHERE [DeduplicationKey] IS NOT NULL;

CREATE INDEX [IX_WorkflowNotifications_RequestId] ON [WorkflowNotifications] ([RequestId]);

CREATE UNIQUE INDEX [IX_WorkflowEmailOutbox_DeduplicationKey] ON [WorkflowEmailOutbox] ([DeduplicationKey]);

CREATE INDEX [IX_WorkflowEmailOutbox_NotificationId] ON [WorkflowEmailOutbox] ([NotificationId]);

CREATE INDEX [IX_WorkflowEmailOutbox_RecipientUserId] ON [WorkflowEmailOutbox] ([RecipientUserId]);

CREATE INDEX [IX_WorkflowEmailOutbox_SentAtUtc_LeaseUntilUtc_CreatedAtUtc] ON [WorkflowEmailOutbox] ([SentAtUtc], [LeaseUntilUtc], [CreatedAtUtc]);

ALTER TABLE [WorkflowNotifications] ADD CONSTRAINT [FK_WorkflowNotifications_ApprovalRequests_RequestId] FOREIGN KEY ([RequestId]) REFERENCES [ApprovalRequests] ([Id]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261020231021_AddWorkflowNotifications', N'10.0.0');

COMMIT;
GO

