IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

IF OBJECT_ID(N'[Agents]', N'U') IS NULL
BEGIN
    CREATE TABLE [Agents] (
        [Id] bigint NOT NULL IDENTITY,
        [AgentName] nvarchar(100) NOT NULL,
        [Scenario] nvarchar(500) NOT NULL,
        [AgentKeyHash] nvarchar(128) NOT NULL,
        [AgentKeyFingerprint] nvarchar(128) NULL,
        [AgentKeyPreview] nvarchar(32) NOT NULL DEFAULT N'',
        [AgentKeySalt] nvarchar(64) NOT NULL,
        [Enabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastUsedAt] datetime2 NULL,
        CONSTRAINT [PK_Agents] PRIMARY KEY ([Id])
    );
END;
GO

IF COL_LENGTH(N'Agents', N'AgentKeyFingerprint') IS NULL
BEGIN
    ALTER TABLE [Agents] ADD [AgentKeyFingerprint] nvarchar(128) NULL;
END;
GO

IF COL_LENGTH(N'Agents', N'AgentKeyPreview') IS NULL
BEGIN
    ALTER TABLE [Agents] ADD [AgentKeyPreview] nvarchar(32) NOT NULL DEFAULT N'';
END;
GO

IF COL_LENGTH(N'Agents', N'OwnerName') IS NOT NULL
BEGIN
    ALTER TABLE [Agents] DROP COLUMN [OwnerName];
END;
GO

IF COL_LENGTH(N'Agents', N'PasswordHash') IS NOT NULL
BEGIN
    ALTER TABLE [Agents] DROP COLUMN [PasswordHash];
END;
GO

IF COL_LENGTH(N'Agents', N'PasswordSalt') IS NOT NULL
BEGIN
    ALTER TABLE [Agents] DROP COLUMN [PasswordSalt];
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_Agents_AgentKeyFingerprint'
      AND [object_id] = OBJECT_ID(N'[Agents]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_Agents_AgentKeyFingerprint]
    ON [Agents] ([AgentKeyFingerprint])
    WHERE [AgentKeyFingerprint] IS NOT NULL;
END;
GO

IF OBJECT_ID(N'[SecurityRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [SecurityRules] (
        [Id] bigint NOT NULL IDENTITY,
        [RuleId] nvarchar(50) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [RuleType] nvarchar(20) NOT NULL,
        [MatchType] nvarchar(20) NOT NULL,
        [Pattern] nvarchar(max) NOT NULL,
        [RiskLevel] nvarchar(30) NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [Replacement] nvarchar(500) NOT NULL DEFAULT N'',
        [Enabled] bit NOT NULL,
        [IsSystemRule] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SecurityRules] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_SecurityRules_RuleId'
      AND [object_id] = OBJECT_ID(N'[SecurityRules]')
)
BEGIN
    CREATE UNIQUE INDEX [IX_SecurityRules_RuleId]
    ON [SecurityRules] ([RuleId]);
END;
GO

IF OBJECT_ID(N'[AgentRules]', N'U') IS NULL
BEGIN
    CREATE TABLE [AgentRules] (
        [AgentId] bigint NOT NULL,
        [RuleId] bigint NOT NULL,
        [Enabled] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AgentRules] PRIMARY KEY ([AgentId], [RuleId]),
        CONSTRAINT [FK_AgentRules_Agents_AgentId] FOREIGN KEY ([AgentId]) REFERENCES [Agents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AgentRules_SecurityRules_RuleId] FOREIGN KEY ([RuleId]) REFERENCES [SecurityRules] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_AgentRules_RuleId'
      AND [object_id] = OBJECT_ID(N'[AgentRules]')
)
BEGIN
    CREATE INDEX [IX_AgentRules_RuleId]
    ON [AgentRules] ([RuleId]);
END;
GO

IF OBJECT_ID(N'[AuditRecords]', N'U') IS NULL
BEGIN
    CREATE TABLE [AuditRecords] (
        [Id] bigint NOT NULL IDENTITY,
        [AgentId] bigint NOT NULL,
        [AgentName] nvarchar(100) NOT NULL,
        [SubjectHash] nvarchar(128) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [Direction] nvarchar(20) NOT NULL,
        [OriginalContent] nvarchar(max) NOT NULL,
        [ProcessedContent] nvarchar(max) NULL,
        [RiskLevel] nvarchar(30) NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [HitRules] nvarchar(500) NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [ClientIp] nvarchar(100) NOT NULL,
        [DurationMs] bigint NOT NULL DEFAULT CAST(0 AS bigint),
        CONSTRAINT [PK_AuditRecords] PRIMARY KEY ([Id])
    );
END;
GO

IF COL_LENGTH(N'AuditRecords', N'AgentId') IS NULL
BEGIN
    ALTER TABLE [AuditRecords] ADD [AgentId] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF COL_LENGTH(N'AuditRecords', N'AgentName') IS NULL
BEGIN
    ALTER TABLE [AuditRecords] ADD [AgentName] nvarchar(100) NOT NULL DEFAULT N'unknown';
END;
GO

IF COL_LENGTH(N'AuditRecords', N'SubjectHash') IS NULL
BEGIN
    ALTER TABLE [AuditRecords] ADD [SubjectHash] nvarchar(128) NULL;
END;
GO

IF COL_LENGTH(N'AuditRecords', N'DurationMs') IS NULL
BEGIN
    ALTER TABLE [AuditRecords] ADD [DurationMs] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF COL_LENGTH(N'AuditRecords', N'AppId') IS NOT NULL
BEGIN
    EXEC(N'UPDATE [AuditRecords] SET [AgentName] = [AppId] WHERE [AgentName] = N''unknown'' OR [AgentName] IS NULL');
    ALTER TABLE [AuditRecords] DROP COLUMN [AppId];
END;
GO

IF COL_LENGTH(N'AuditRecords', N'UserId') IS NOT NULL
BEGIN
    ALTER TABLE [AuditRecords] DROP COLUMN [UserId];
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_AuditRecords_AgentId'
      AND [object_id] = OBJECT_ID(N'[AuditRecords]')
)
BEGIN
    CREATE INDEX [IX_AuditRecords_AgentId]
    ON [AuditRecords] ([AgentId]);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_AuditRecords_CreatedAt'
      AND [object_id] = OBJECT_ID(N'[AuditRecords]')
)
BEGIN
    CREATE INDEX [IX_AuditRecords_CreatedAt]
    ON [AuditRecords] ([CreatedAt]);
END;
GO
