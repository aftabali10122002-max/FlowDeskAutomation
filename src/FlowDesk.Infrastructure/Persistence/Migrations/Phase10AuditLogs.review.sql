BEGIN TRANSACTION;
CREATE TABLE [AuditLogEntries] (
    [Id] bigint NOT NULL IDENTITY,
    [ActorUserId] int NOT NULL,
    [Action] nvarchar(60) NOT NULL,
    [EntityType] nvarchar(60) NOT NULL,
    [EntityId] bigint NOT NULL,
    [OccurredAtUtc] datetime2 NOT NULL,
    [Details] nvarchar(500) NULL,
    CONSTRAINT [PK_AuditLogEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AuditLogEntries_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([Id])
);

CREATE INDEX [IX_AuditLogEntries_Action_OccurredAtUtc] ON [AuditLogEntries] ([Action], [OccurredAtUtc]);

CREATE INDEX [IX_AuditLogEntries_ActorUserId] ON [AuditLogEntries] ([ActorUserId]);

CREATE INDEX [IX_AuditLogEntries_EntityType_EntityId] ON [AuditLogEntries] ([EntityType], [EntityId]);

CREATE INDEX [IX_AuditLogEntries_OccurredAtUtc] ON [AuditLogEntries] ([OccurredAtUtc]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261028151134_Phase10AuditLogs', N'10.0.0');

COMMIT;
GO

