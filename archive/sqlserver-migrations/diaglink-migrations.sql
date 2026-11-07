IF OBJECT_ID(N'[chat].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'chat') IS NULL EXEC(N'CREATE SCHEMA [chat];');
    CREATE TABLE [chat].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    IF SCHEMA_ID(N'chat') IS NULL EXEC(N'CREATE SCHEMA [chat];');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    CREATE TABLE [chat].[Conversations] (
        [Id] uniqueidentifier NOT NULL,
        [FoundryConversationId] nvarchar(200) NOT NULL,
        [UserObjectId] nvarchar(200) NOT NULL,
        [Entreprise] nvarchar(max) NULL,
        [Machine] nvarchar(max) NULL,
        [AgentName] nvarchar(max) NULL,
        [TechnicalSummary] nvarchar(max) NULL,
        [TechnicalStateJson] nvarchar(max) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    CREATE TABLE [chat].[ConversationMessages] (
        [Id] bigint NOT NULL IDENTITY,
        [ConversationId] uniqueidentifier NOT NULL,
        [Role] nvarchar(50) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [TokenCount] int NULL,
        [IsSummarized] bit NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ConversationMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConversationMessages_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [chat].[Conversations] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    CREATE INDEX [IX_ConversationMessages_ConversationId] ON [chat].[ConversationMessages] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    CREATE INDEX [IX_Conversations_CreatedAtUtc] ON [chat].[Conversations] ([CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    CREATE INDEX [IX_Conversations_FoundryConversationId] ON [chat].[Conversations] ([FoundryConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserObjectId] ON [chat].[Conversations] ([UserObjectId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260830193136_InitialConversationHistory'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260830193136_InitialConversationHistory', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831195319_AddLoginCodes'
)
BEGIN
    CREATE TABLE [chat].[LoginCodes] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CodeHash] nvarchar(200) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [UsedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_LoginCodes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831195319_AddLoginCodes'
)
BEGIN
    CREATE INDEX [IX_LoginCodes_ExpiresAtUtc] ON [chat].[LoginCodes] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831195319_AddLoginCodes'
)
BEGIN
    CREATE INDEX [IX_LoginCodes_UserId] ON [chat].[LoginCodes] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831195319_AddLoginCodes'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831195319_AddLoginCodes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901091647_AddUserSessions'
)
BEGIN
    CREATE TABLE [chat].[UserSessions] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(200) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_UserSessions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901091647_AddUserSessions'
)
BEGIN
    CREATE INDEX [IX_UserSessions_ExpiresAtUtc] ON [chat].[UserSessions] ([ExpiresAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901091647_AddUserSessions'
)
BEGIN
    CREATE INDEX [IX_UserSessions_TokenHash] ON [chat].[UserSessions] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901091647_AddUserSessions'
)
BEGIN
    CREATE INDEX [IX_UserSessions_UserId] ON [chat].[UserSessions] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901091647_AddUserSessions'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901091647_AddUserSessions', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE TABLE [chat].[Companies] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE TABLE [chat].[Machines] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Manufacturer] nvarchar(200) NULL,
        [Model] nvarchar(200) NULL,
        [SerialNumber] nvarchar(200) NULL,
        [Status] nvarchar(50) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Machines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Machines_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [chat].[Companies] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE TABLE [chat].[UserMachineAccess] (
        [UserId] uniqueidentifier NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_UserMachineAccess] PRIMARY KEY ([UserId], [MachineId]),
        CONSTRAINT [FK_UserMachineAccess_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [chat].[Machines] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE INDEX [IX_Companies_Name] ON [chat].[Companies] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE INDEX [IX_Machines_CompanyId] ON [chat].[Machines] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE INDEX [IX_Machines_CompanyId_Name] ON [chat].[Machines] ([CompanyId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE INDEX [IX_Machines_SerialNumber] ON [chat].[Machines] ([SerialNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE INDEX [IX_UserMachineAccess_MachineId] ON [chat].[UserMachineAccess] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    CREATE INDEX [IX_UserMachineAccess_UserId] ON [chat].[UserMachineAccess] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901133312_AddCompanyMachineModel'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901133312_AddCompanyMachineModel', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId'
)
BEGIN
    ALTER TABLE [chat].[Conversations] ADD [MachineId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId'
)
BEGIN
    CREATE TABLE [chat].[MachineAssistantConfigurations] (
        [Id] uniqueidentifier NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(50) NOT NULL,
        [ProjectEndpoint] nvarchar(500) NOT NULL,
        [AgentId] nvarchar(200) NOT NULL,
        [AgentName] nvarchar(200) NULL,
        [AgentVersion] nvarchar(50) NULL,
        [VectorStoreId] nvarchar(200) NULL,
        [Status] nvarchar(50) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MachineAssistantConfigurations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MachineAssistantConfigurations_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [chat].[Machines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId'
)
BEGIN
    CREATE INDEX [IX_Conversations_MachineId] ON [chat].[Conversations] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MachineAssistantConfigurations_MachineId] ON [chat].[MachineAssistantConfigurations] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId'
)
BEGIN
    ALTER TABLE [chat].[Conversations] ADD CONSTRAINT [FK_Conversations_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [chat].[Machines] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901154059_AddMachineAssistantConfigurationAndConversationMachineId', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903001255_FilterUsersEntraObjectIdUniqueIndex'
)
BEGIN
    DECLARE @usersObjectId int = OBJECT_ID(N'[dbo].[Users]');
    DECLARE @constraintName sysname;
    DECLARE @indexName sysname;
    DECLARE @sql nvarchar(max);

    SELECT TOP (1) @constraintName = kc.[name]
    FROM sys.key_constraints AS kc
    WHERE kc.parent_object_id = @usersObjectId
      AND kc.[type] = N'UQ'
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns AS ic
          INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id
             AND c.column_id = ic.column_id
          WHERE ic.object_id = kc.parent_object_id
            AND ic.index_id = kc.unique_index_id
            AND ic.key_ordinal > 0
            AND c.[name] = N'EntraObjectId')
      AND NOT EXISTS (
          SELECT 1
          FROM sys.index_columns AS ic
          INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id
             AND c.column_id = ic.column_id
          WHERE ic.object_id = kc.parent_object_id
            AND ic.index_id = kc.unique_index_id
            AND ic.key_ordinal > 0
            AND c.[name] <> N'EntraObjectId');

    IF @constraintName IS NOT NULL
    BEGIN
        SET @sql = N'ALTER TABLE [dbo].[Users] DROP CONSTRAINT ' + QUOTENAME(@constraintName);
        EXEC sp_executesql @sql;
    END;

    SELECT TOP (1) @indexName = i.[name]
    FROM sys.indexes AS i
    WHERE i.object_id = @usersObjectId
      AND i.is_unique = 1
      AND i.is_primary_key = 0
      AND i.is_unique_constraint = 0
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns AS ic
          INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id
             AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND ic.key_ordinal > 0
            AND c.[name] = N'EntraObjectId')
      AND NOT EXISTS (
          SELECT 1
          FROM sys.index_columns AS ic
          INNER JOIN sys.columns AS c
              ON c.object_id = ic.object_id
             AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND ic.key_ordinal > 0
            AND c.[name] <> N'EntraObjectId')
      AND (i.filter_definition IS NULL OR i.filter_definition <> N'([EntraObjectId] IS NOT NULL)');

    IF @indexName IS NOT NULL
    BEGIN
        SET @sql = N'DROP INDEX ' + QUOTENAME(@indexName) + N' ON [dbo].[Users]';
        EXEC sp_executesql @sql;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = @usersObjectId
          AND [name] = N'IX_Users_EntraObjectId_NotNull')
    BEGIN
        CREATE UNIQUE INDEX [IX_Users_EntraObjectId_NotNull]
        ON [dbo].[Users] ([EntraObjectId])
        WHERE [EntraObjectId] IS NOT NULL;
    END;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903001255_FilterUsersEntraObjectIdUniqueIndex'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903001255_FilterUsersEntraObjectIdUniqueIndex', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    CREATE TABLE [chat].[AiUsageRecords] (
        [Id] uniqueidentifier NOT NULL,
        [UsageType] nvarchar(32) NOT NULL,
        [Available] bit NOT NULL,
        [Completed] bit NOT NULL,
        [UserId] uniqueidentifier NULL,
        [CompanyId] uniqueidentifier NULL,
        [MachineId] uniqueidentifier NULL,
        [ConversationId] uniqueidentifier NULL,
        [AssistantMessageId] bigint NULL,
        [FoundryConversationId] nvarchar(200) NULL,
        [ResponseId] nvarchar(200) NULL,
        [Model] nvarchar(256) NULL,
        [ModelSource] nvarchar(32) NULL,
        [AgentVersion] nvarchar(100) NULL,
        [InputTokens] int NULL,
        [OutputTokens] int NULL,
        [TotalTokens] int NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AiUsageRecords] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    CREATE INDEX [IX_AiUsageRecords_CompanyId_CreatedAtUtc] ON [chat].[AiUsageRecords] ([CompanyId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    CREATE INDEX [IX_AiUsageRecords_CreatedAtUtc] ON [chat].[AiUsageRecords] ([CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    CREATE INDEX [IX_AiUsageRecords_MachineId_CreatedAtUtc] ON [chat].[AiUsageRecords] ([MachineId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    CREATE INDEX [IX_AiUsageRecords_UsageType_CreatedAtUtc] ON [chat].[AiUsageRecords] ([UsageType], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    CREATE INDEX [IX_AiUsageRecords_UserId_CreatedAtUtc] ON [chat].[AiUsageRecords] ([UserId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260908151451_AddAiUsageRecords'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260908151451_AddAiUsageRecords', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909061328_AddVisionUsageCorrelation'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [CallId] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909061328_AddVisionUsageCorrelation'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [ParentResponseId] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909061328_AddVisionUsageCorrelation'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909061328_AddVisionUsageCorrelation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE TABLE [dbo].[AiPricing] (
        [Id] uniqueidentifier NOT NULL,
        [Provider] nvarchar(100) NOT NULL,
        [Model] nvarchar(256) NOT NULL,
        [UsageType] nvarchar(32) NULL,
        [InputPricePerMillion] decimal(18,8) NOT NULL,
        [OutputPricePerMillion] decimal(18,8) NOT NULL,
        [Currency] varchar(3) NOT NULL,
        [EffectiveFromUtc] datetime2 NOT NULL,
        [EffectiveToUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_AiPricing] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE TABLE [dbo].[BillingAccounts] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [StripeCustomerId] nvarchar(200) NULL,
        [StripeSubscriptionId] nvarchar(200) NULL,
        [SubscriptionStatus] nvarchar(50) NULL,
        [CurrentPeriodStartUtc] datetime2 NULL,
        [CurrentPeriodEndUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_BillingAccounts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BillingAccounts_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE TABLE [dbo].[CompanyWallets] (
        [CompanyId] uniqueidentifier NOT NULL,
        [Balance] decimal(18,6) NOT NULL,
        [Currency] varchar(3) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_CompanyWallets] PRIMARY KEY ([CompanyId]),
        CONSTRAINT [FK_CompanyWallets_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE TABLE [dbo].[MachineBillingPeriods] (
        [Id] uniqueidentifier NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [PeriodStartUtc] datetime2 NOT NULL,
        [PeriodEndUtc] datetime2 NOT NULL,
        [IncludedAiBudgetRealCost] decimal(18,6) NOT NULL,
        [IncludedAiUsedRealCost] decimal(18,6) NOT NULL DEFAULT 0.0,
        [Status] nvarchar(50) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_MachineBillingPeriods] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MachineBillingPeriods_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [dbo].[Machines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE TABLE [dbo].[CreditLedger] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [MachineId] uniqueidentifier NULL,
        [MachineBillingPeriodId] uniqueidentifier NULL,
        [AiUsageRecordId] uniqueidentifier NULL,
        [EntryType] nvarchar(50) NOT NULL,
        [BucketType] nvarchar(50) NOT NULL,
        [RealAiCost] decimal(18,6) NULL,
        [CommercialCreditAmount] decimal(18,6) NULL,
        [BalanceAfter] decimal(18,6) NULL,
        [Currency] varchar(3) NULL,
        [ExternalEventId] nvarchar(200) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [Notes] nvarchar(2000) NULL,
        CONSTRAINT [PK_CreditLedger] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CreditLedger_AiUsageRecords_AiUsageRecordId] FOREIGN KEY ([AiUsageRecordId]) REFERENCES [chat].[AiUsageRecords] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CreditLedger_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CreditLedger_MachineBillingPeriods_MachineBillingPeriodId] FOREIGN KEY ([MachineBillingPeriodId]) REFERENCES [dbo].[MachineBillingPeriods] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CreditLedger_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [dbo].[Machines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_AiPricing_Provider_Model_UsageType_EffectiveFromUtc] ON [dbo].[AiPricing] ([Provider], [Model], [UsageType], [EffectiveFromUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BillingAccounts_CompanyId] ON [dbo].[BillingAccounts] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BillingAccounts_StripeCustomerId] ON [dbo].[BillingAccounts] ([StripeCustomerId]) WHERE [StripeCustomerId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_BillingAccounts_StripeSubscriptionId] ON [dbo].[BillingAccounts] ([StripeSubscriptionId]) WHERE [StripeSubscriptionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_CreditLedger_AiUsageRecordId] ON [dbo].[CreditLedger] ([AiUsageRecordId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_CreditLedger_CompanyId_CreatedAtUtc] ON [dbo].[CreditLedger] ([CompanyId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_CreditLedger_ExternalEventId] ON [dbo].[CreditLedger] ([ExternalEventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_CreditLedger_MachineBillingPeriodId] ON [dbo].[CreditLedger] ([MachineBillingPeriodId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_CreditLedger_MachineId_CreatedAtUtc] ON [dbo].[CreditLedger] ([MachineId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE INDEX [IX_MachineBillingPeriods_MachineId] ON [dbo].[MachineBillingPeriods] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MachineBillingPeriods_MachineId_PeriodStartUtc] ON [dbo].[MachineBillingPeriods] ([MachineId], [PeriodStartUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909222945_AddBillingAndCreditFoundation'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909222945_AddBillingAndCreditFoundation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909224730_AddAiUsagePricingIdentity'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [Deployment] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909224730_AddAiUsagePricingIdentity'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [Provider] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260909224730_AddAiUsagePricingIdentity'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260909224730_AddAiUsagePricingIdentity', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910121907_AddExchangeRates'
)
BEGIN
    CREATE TABLE [dbo].[ExchangeRates] (
        [Id] uniqueidentifier NOT NULL,
        [BaseCurrency] varchar(3) NOT NULL,
        [QuoteCurrency] varchar(3) NOT NULL,
        [Rate] decimal(18,10) NOT NULL,
        [EffectiveFromUtc] datetime2 NOT NULL,
        [EffectiveToUtc] datetime2 NULL,
        [Source] nvarchar(200) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ExchangeRates] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ExchangeRates_Interval] CHECK ([EffectiveToUtc] IS NULL OR [EffectiveToUtc] > [EffectiveFromUtc]),
        CONSTRAINT [CK_ExchangeRates_Rate] CHECK ([Rate] > 0)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910121907_AddExchangeRates'
)
BEGIN
    CREATE INDEX [IX_ExchangeRates_BaseCurrency_QuoteCurrency_EffectiveFromUtc] ON [dbo].[ExchangeRates] ([BaseCurrency], [QuoteCurrency], [EffectiveFromUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910121907_AddExchangeRates'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910121907_AddExchangeRates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910123357_AddMachineIncludedLedgerIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_CreditLedger_MachineIncludedUsage] ON [dbo].[CreditLedger] ([AiUsageRecordId]) WHERE [AiUsageRecordId] IS NOT NULL AND [BucketType] = ''MachineIncluded'' AND [EntryType] = ''AiUsage''');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910123357_AddMachineIncludedLedgerIdempotency'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910123357_AddMachineIncludedLedgerIdempotency', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910165041_AddCompanyWalletLedgerIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_CreditLedger_CompanyWalletUsage] ON [dbo].[CreditLedger] ([AiUsageRecordId]) WHERE [AiUsageRecordId] IS NOT NULL AND [BucketType] = ''CompanyWallet'' AND [EntryType] = ''AiUsage''');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910165041_AddCompanyWalletLedgerIdempotency'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910165041_AddCompanyWalletLedgerIdempotency', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911080136_AddCreditLedgerExternalEventIdempotency'
)
BEGIN
    DROP INDEX [IX_CreditLedger_ExternalEventId] ON [dbo].[CreditLedger];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911080136_AddCreditLedgerExternalEventIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CreditLedger_ExternalEventId] ON [dbo].[CreditLedger] ([ExternalEventId]) WHERE [ExternalEventId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911080136_AddCreditLedgerExternalEventIdempotency'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260911080136_AddCreditLedgerExternalEventIdempotency', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    CREATE TABLE [dbo].[StripeMachineAdditions] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [MachineId] uniqueidentifier NOT NULL,
        [BillingAccountId] uniqueidentifier NOT NULL,
        [ActivatedAtUtc] datetime2 NOT NULL,
        [CycleStartUtc] datetime2 NOT NULL,
        [CycleEndUtc] datetime2 NOT NULL,
        [StripeCustomerId] nvarchar(200) NOT NULL,
        [StripeSubscriptionId] nvarchar(200) NOT NULL,
        [StripeSubscriptionItemId] nvarchar(200) NOT NULL,
        [StripePriceId] nvarchar(200) NOT NULL,
        [OriginalQuantity] bigint NOT NULL,
        [TargetQuantity] bigint NOT NULL,
        [AiAmountCents] int NOT NULL,
        [ServiceAmountCents] int NOT NULL,
        [Stage] int NOT NULL,
        [MachineBillingPeriodId] uniqueidentifier NULL,
        [StripeInvoiceId] nvarchar(200) NULL,
        [PaymentReference] nvarchar(2000) NULL,
        [PaymentConfirmedAtUtc] datetime2 NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_StripeMachineAdditions] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StripeMachineAdditions_Amounts] CHECK ([AiAmountCents] = 1000 AND [ServiceAmountCents] >= 0 AND [ServiceAmountCents] <= 1990),
        CONSTRAINT [CK_StripeMachineAdditions_Cycle] CHECK ([CycleStartUtc] <= [ActivatedAtUtc] AND [ActivatedAtUtc] < [CycleEndUtc]),
        CONSTRAINT [CK_StripeMachineAdditions_Quantity] CHECK ([OriginalQuantity] > 0 AND [TargetQuantity] = [OriginalQuantity] + 1),
        CONSTRAINT [CK_StripeMachineAdditions_Stage] CHECK ([Stage] >= 0 AND [Stage] <= 6),
        CONSTRAINT [CK_StripeMachineAdditions_Payment] CHECK (([Stage] < 4 AND [PaymentReference] IS NULL AND [PaymentConfirmedAtUtc] IS NULL) OR ([Stage] >= 4 AND [PaymentReference] IS NOT NULL AND [PaymentReference] <> '' AND [PaymentConfirmedAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_StripeMachineAdditions_Period] CHECK (([Stage] < 5 AND [MachineBillingPeriodId] IS NULL) OR ([Stage] >= 5 AND [MachineBillingPeriodId] IS NOT NULL)),
        CONSTRAINT [CK_StripeMachineAdditions_Completed] CHECK (([Stage] < 6 AND [CompletedAtUtc] IS NULL) OR ([Stage] = 6 AND [CompletedAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_StripeMachineAdditions_Invoice] CHECK ([Stage] < 2 OR [StripeInvoiceId] IS NOT NULL),
        CONSTRAINT [FK_StripeMachineAdditions_BillingAccounts_BillingAccountId] FOREIGN KEY ([BillingAccountId]) REFERENCES [dbo].[BillingAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StripeMachineAdditions_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StripeMachineAdditions_MachineBillingPeriods_MachineBillingPeriodId] FOREIGN KEY ([MachineBillingPeriodId]) REFERENCES [dbo].[MachineBillingPeriods] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StripeMachineAdditions_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [dbo].[Machines] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    CREATE INDEX [IX_StripeMachineAdditions_BillingAccountId] ON [dbo].[StripeMachineAdditions] ([BillingAccountId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeMachineAdditions_CompanyId] ON [dbo].[StripeMachineAdditions] ([CompanyId]) WHERE [CompletedAtUtc] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    CREATE INDEX [IX_StripeMachineAdditions_MachineBillingPeriodId] ON [dbo].[StripeMachineAdditions] ([MachineBillingPeriodId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StripeMachineAdditions_MachineId] ON [dbo].[StripeMachineAdditions] ([MachineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeMachineAdditions_StripeInvoiceId] ON [dbo].[StripeMachineAdditions] ([StripeInvoiceId]) WHERE [StripeInvoiceId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911083148_AddStripeMachineAdditionOperations'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260911083148_AddStripeMachineAdditionOperations', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911124704_AddStripeMachineAdditionPaymentEvent'
)
BEGIN
    ALTER TABLE [dbo].[StripeMachineAdditions] ADD [ExternalEventId] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911124704_AddStripeMachineAdditionPaymentEvent'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeMachineAdditions_ExternalEventId] ON [dbo].[StripeMachineAdditions] ([ExternalEventId]) WHERE [ExternalEventId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911124704_AddStripeMachineAdditionPaymentEvent'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260911124704_AddStripeMachineAdditionPaymentEvent', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    EXEC(N'CREATE TRIGGER [dbo].[TR_CreditLedger_TopUpImmutable]
    ON [dbo].[CreditLedger] AFTER UPDATE, DELETE AS
    BEGIN
        SET NOCOUNT ON;
        IF EXISTS (SELECT 1 FROM deleted WHERE [EntryType] = ''TopUp'')
            THROW 51001, ''TopUp ledger entries are immutable.'', 1;
    END');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    CREATE TABLE [dbo].[StripeWalletTopUps] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [StripeCustomerId] nvarchar(200) NOT NULL,
        [AmountCents] int NOT NULL,
        [Currency] varchar(3) NOT NULL,
        [ReturnUrl] nvarchar(2048) NOT NULL,
        [Stage] int NOT NULL,
        [StripeSessionId] nvarchar(200) NULL,
        [PaymentUrl] nvarchar(2048) NULL,
        [StripePaymentIntentId] nvarchar(200) NULL,
        [ExternalEventId] nvarchar(200) NULL,
        [PaymentConfirmedAtUtc] datetime2 NULL,
        [LedgerEntryId] uniqueidentifier NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_StripeWalletTopUps] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StripeWalletTopUps_Amount] CHECK ([AmountCents] >= 1000 AND [AmountCents] <= 99999999 AND [Currency] = 'EUR'),
        CONSTRAINT [CK_StripeWalletTopUps_Completed] CHECK (([Stage] < 5 AND [CompletedAtUtc] IS NULL) OR ([Stage] = 5 AND [CompletedAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_StripeWalletTopUps_Ledger] CHECK (([Stage] < 4 AND [LedgerEntryId] IS NULL) OR ([Stage] >= 4 AND [LedgerEntryId] IS NOT NULL)),
        CONSTRAINT [CK_StripeWalletTopUps_Payment] CHECK (([Stage] < 3 AND [StripePaymentIntentId] IS NULL AND [PaymentConfirmedAtUtc] IS NULL) OR ([Stage] >= 3 AND [StripePaymentIntentId] IS NOT NULL AND [PaymentConfirmedAtUtc] IS NOT NULL AND [ExternalEventId] IS NOT NULL)),
        CONSTRAINT [CK_StripeWalletTopUps_Session] CHECK ([Stage] = 0 OR [StripeSessionId] IS NOT NULL),
        CONSTRAINT [CK_StripeWalletTopUps_Stage] CHECK ([Stage] >= 0 AND [Stage] <= 5),
        CONSTRAINT [FK_StripeWalletTopUps_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_StripeWalletTopUps_CreditLedger_LedgerEntryId] FOREIGN KEY ([LedgerEntryId]) REFERENCES [dbo].[CreditLedger] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    CREATE INDEX [IX_StripeWalletTopUps_CompanyId_CreatedAtUtc] ON [dbo].[StripeWalletTopUps] ([CompanyId], [CreatedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeWalletTopUps_ExternalEventId] ON [dbo].[StripeWalletTopUps] ([ExternalEventId]) WHERE [ExternalEventId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeWalletTopUps_LedgerEntryId] ON [dbo].[StripeWalletTopUps] ([LedgerEntryId]) WHERE [LedgerEntryId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeWalletTopUps_StripePaymentIntentId] ON [dbo].[StripeWalletTopUps] ([StripePaymentIntentId]) WHERE [StripePaymentIntentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_StripeWalletTopUps_StripeSessionId] ON [dbo].[StripeWalletTopUps] ([StripeSessionId]) WHERE [StripeSessionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260911143401_AddStripeWalletTopUps'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260911143401_AddStripeWalletTopUps', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914134817_AddStripeSubscriptionPayments'
)
BEGIN
    CREATE TABLE [dbo].[StripeSubscriptionPayments] (
        [Id] uniqueidentifier NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [StripeSubscriptionId] nvarchar(200) NOT NULL,
        [StripeInvoiceId] nvarchar(200) NOT NULL,
        [ExternalEventId] nvarchar(200) NOT NULL,
        [BillingReason] nvarchar(40) NOT NULL,
        [PeriodStartUtc] datetime2 NOT NULL,
        [PeriodEndUtc] datetime2 NOT NULL,
        [PaymentConfirmedAtUtc] datetime2 NOT NULL,
        [AmountPaidCents] bigint NOT NULL,
        [PaymentReference] nvarchar(2000) NOT NULL,
        [MachineIdsJson] nvarchar(max) NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_StripeSubscriptionPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_StripeSubscriptionPayments_Amount] CHECK ([AmountPaidCents] > 0),
        CONSTRAINT [CK_StripeSubscriptionPayments_Cycle] CHECK ([PeriodEndUtc] > [PeriodStartUtc]),
        CONSTRAINT [CK_StripeSubscriptionPayments_Reason] CHECK ([BillingReason] IN ('subscription_create', 'subscription_cycle')),
        CONSTRAINT [FK_StripeSubscriptionPayments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914134817_AddStripeSubscriptionPayments'
)
BEGIN
    CREATE INDEX [IX_StripeSubscriptionPayments_CompanyId] ON [dbo].[StripeSubscriptionPayments] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914134817_AddStripeSubscriptionPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StripeSubscriptionPayments_ExternalEventId] ON [dbo].[StripeSubscriptionPayments] ([ExternalEventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914134817_AddStripeSubscriptionPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StripeSubscriptionPayments_StripeInvoiceId] ON [dbo].[StripeSubscriptionPayments] ([StripeInvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914134817_AddStripeSubscriptionPayments'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StripeSubscriptionPayments_StripeSubscriptionId_PeriodStartUtc] ON [dbo].[StripeSubscriptionPayments] ([StripeSubscriptionId], [PeriodStartUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914134817_AddStripeSubscriptionPayments'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260914134817_AddStripeSubscriptionPayments', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    DROP INDEX [IX_StripeMachineAdditions_MachineId] ON [dbo].[StripeMachineAdditions];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    ALTER TABLE [dbo].[StripeMachineAdditions] DROP CONSTRAINT [CK_StripeMachineAdditions_Quantity];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    ALTER TABLE [dbo].[BillingAccounts] ADD [AmountRemainingCents] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    ALTER TABLE [dbo].[BillingAccounts] ADD [CancelAtPeriodEnd] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    ALTER TABLE [dbo].[BillingAccounts] ADD [LatestInvoiceId] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    ALTER TABLE [dbo].[BillingAccounts] ADD [LatestInvoiceStatus] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    CREATE TABLE [dbo].[StripeLifecycleEvents] (
        [Id] nvarchar(200) NOT NULL,
        [CompanyId] uniqueidentifier NOT NULL,
        [SubscriptionId] nvarchar(200) NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [MachineId] uniqueidentifier NULL,
        [CompletedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_StripeLifecycleEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StripeLifecycleEvents_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StripeMachineAdditions_MachineId_CycleStartUtc] ON [dbo].[StripeMachineAdditions] ([MachineId], [CycleStartUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[StripeMachineAdditions] ADD CONSTRAINT [CK_StripeMachineAdditions_Quantity] CHECK ([OriginalQuantity] >= 0 AND [TargetQuantity] = [OriginalQuantity] + 1)');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    CREATE INDEX [IX_StripeLifecycleEvents_CompanyId] ON [dbo].[StripeLifecycleEvents] ([CompanyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260914193125_AddStripeLifecycle'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260914193125_AddStripeLifecycle', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922154250_AddMachineRequestPayments'
)
BEGIN
    CREATE TABLE [dbo].[MachineRequestPayments] (
        [Id] uniqueidentifier NOT NULL,
        [Status] int NOT NULL,
        [EstimatedTotalPages] int NOT NULL,
        [AmountCents] bigint NOT NULL,
        [Currency] varchar(3) NOT NULL,
        [Email] nvarchar(320) NULL,
        [StripeSessionId] nvarchar(200) NULL,
        [StripePaymentIntentId] nvarchar(200) NULL,
        [AuthorizationEventId] nvarchar(200) NULL,
        [MachineRequestId] varchar(32) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        [AuthorizedAtUtc] datetime2 NULL,
        [CapturedAtUtc] datetime2 NULL,
        [CancelledAtUtc] datetime2 NULL,
        [RequestLinkedAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_MachineRequestPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_MachineRequestPayments_Amount] CHECK ([AmountCents] > 0),
        CONSTRAINT [CK_MachineRequestPayments_Currency] CHECK ([Currency] = 'EUR'),
        CONSTRAINT [CK_MachineRequestPayments_Link] CHECK (([MachineRequestId] IS NULL AND [RequestLinkedAtUtc] IS NULL) OR ([MachineRequestId] IS NOT NULL AND [RequestLinkedAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_MachineRequestPayments_Pages] CHECK ([EstimatedTotalPages] > 0),
        CONSTRAINT [CK_MachineRequestPayments_State] CHECK (([Status] = 0 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 1 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 2 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 3 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_MachineRequestPayments_Status] CHECK ([Status] >= 0 AND [Status] <= 3),
        CONSTRAINT [CK_MachineRequestPayments_Timestamps] CHECK ([UpdatedAtUtc] >= [CreatedAtUtc])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922154250_AddMachineRequestPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MachineRequestPayments_AuthorizationEventId] ON [dbo].[MachineRequestPayments] ([AuthorizationEventId]) WHERE [AuthorizationEventId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922154250_AddMachineRequestPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MachineRequestPayments_MachineRequestId] ON [dbo].[MachineRequestPayments] ([MachineRequestId]) WHERE [MachineRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922154250_AddMachineRequestPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MachineRequestPayments_StripePaymentIntentId] ON [dbo].[MachineRequestPayments] ([StripePaymentIntentId]) WHERE [StripePaymentIntentId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922154250_AddMachineRequestPayments'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MachineRequestPayments_StripeSessionId] ON [dbo].[MachineRequestPayments] ([StripeSessionId]) WHERE [StripeSessionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922154250_AddMachineRequestPayments'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922154250_AddMachineRequestPayments', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [ActivatedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [CompanyId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [FinalCaptureAmountCents] bigint NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [FirstPeriodEndUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [MachineId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [ProvisioningCompletedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [ProvisioningStage] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [ServiceAmountCents] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_MachineRequestPayments_CompanyId] ON [dbo].[MachineRequestPayments] ([CompanyId]) WHERE [CompanyId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MachineRequestPayments_MachineId] ON [dbo].[MachineRequestPayments] ([MachineId]) WHERE [MachineId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_FinalCaptureAmount] CHECK ([FinalCaptureAmountCents] IS NULL OR ([FinalCaptureAmountCents] > 0 AND [FinalCaptureAmountCents] <= [AmountCents]))');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_FirstPeriod] CHECK (([ActivatedAtUtc] IS NULL AND [FirstPeriodEndUtc] IS NULL) OR ([ActivatedAtUtc] IS NOT NULL AND [FirstPeriodEndUtc] IS NOT NULL AND [ActivatedAtUtc] < [FirstPeriodEndUtc]))');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_ProvisioningCompleted] CHECK (([ProvisioningStage] < 6 AND [ProvisioningCompletedAtUtc] IS NULL) OR ([ProvisioningStage] = 6 AND [ProvisioningCompletedAtUtc] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_ProvisioningStage] CHECK ([ProvisioningStage] >= 0 AND [ProvisioningStage] <= 6)');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_ServiceAmount] CHECK ([ServiceAmountCents] IS NULL OR ([ServiceAmountCents] >= 0 AND [ServiceAmountCents] <= 1990))');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [FK_MachineRequestPayments_Companies_CompanyId] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [FK_MachineRequestPayments_Machines_MachineId] FOREIGN KEY ([MachineId]) REFERENCES [dbo].[Machines] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260922214955_AddMachineRequestProvisioningFoundation'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260922214955_AddMachineRequestProvisioningFoundation', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924001734_AddMachineRequestOrigin'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [RequestKind] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924001734_AddMachineRequestOrigin'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [RequestedByUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924001734_AddMachineRequestOrigin'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_RequestKind] CHECK ([RequestKind] >= 0 AND [RequestKind] <= 1)');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924001734_AddMachineRequestOrigin'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924001734_AddMachineRequestOrigin', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924173002_AddAdditionalDocumentsRequestModel'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] DROP CONSTRAINT [CK_MachineRequestPayments_RequestKind];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924173002_AddAdditionalDocumentsRequestModel'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [TargetMachineId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924173002_AddAdditionalDocumentsRequestModel'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_MachineRequestPayments_TargetMachineId] ON [dbo].[MachineRequestPayments] ([TargetMachineId]) WHERE [TargetMachineId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924173002_AddAdditionalDocumentsRequestModel'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_RequestKind] CHECK ([RequestKind] >= 0 AND [RequestKind] <= 2)');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924173002_AddAdditionalDocumentsRequestModel'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [FK_MachineRequestPayments_Machines_TargetMachineId] FOREIGN KEY ([TargetMachineId]) REFERENCES [dbo].[Machines] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260924173002_AddAdditionalDocumentsRequestModel'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260924173002_AddAdditionalDocumentsRequestModel', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115443_AddMachineRequestPaymentAbandonedState'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] DROP CONSTRAINT [CK_MachineRequestPayments_State];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115443_AddMachineRequestPaymentAbandonedState'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] DROP CONSTRAINT [CK_MachineRequestPayments_Status];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115443_AddMachineRequestPaymentAbandonedState'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_State] CHECK (([Status] = 0 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 1 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 2 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 3 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL) OR ([Status] = 4 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115443_AddMachineRequestPaymentAbandonedState'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_Status] CHECK ([Status] >= 0 AND [Status] <= 4)');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925115443_AddMachineRequestPaymentAbandonedState'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925115443_AddMachineRequestPaymentAbandonedState', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925183344_AddEmailOutbox'
)
BEGIN
    CREATE TABLE [dbo].[EmailOutbox] (
        [Id] uniqueidentifier NOT NULL,
        [MachineRequestId] varchar(32) NOT NULL,
        [PaymentRequestId] uniqueidentifier NULL,
        [NotificationType] int NOT NULL,
        [RecipientEmail] nvarchar(320) NOT NULL,
        [RecipientName] nvarchar(200) NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [Status] int NOT NULL,
        [AttemptCount] int NOT NULL,
        [NextAttemptAtUtc] datetime2 NOT NULL,
        [LeaseId] uniqueidentifier NULL,
        [LockedUntilUtc] datetime2 NULL,
        [LastAttemptAtUtc] datetime2 NULL,
        [SentAtUtc] datetime2 NULL,
        [ProviderOperationId] nvarchar(200) NULL,
        [LastError] nvarchar(1000) NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_EmailOutbox] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_EmailOutbox_AttemptCount] CHECK ([AttemptCount] >= 0),
        CONSTRAINT [CK_EmailOutbox_Lease] CHECK (([LeaseId] IS NULL AND [LockedUntilUtc] IS NULL) OR ([LeaseId] IS NOT NULL AND [LockedUntilUtc] IS NOT NULL)),
        CONSTRAINT [CK_EmailOutbox_NotificationType] CHECK ([NotificationType] >= 0 AND [NotificationType] <= 4),
        CONSTRAINT [CK_EmailOutbox_Sent] CHECK (([Status] = 0 AND [SentAtUtc] IS NULL) OR ([Status] = 1 AND [SentAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_EmailOutbox_Status] CHECK ([Status] >= 0 AND [Status] <= 1)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925183344_AddEmailOutbox'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmailOutbox_MachineRequestId_NotificationType] ON [dbo].[EmailOutbox] ([MachineRequestId], [NotificationType]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925183344_AddEmailOutbox'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_EmailOutbox_PaymentRequestId] ON [dbo].[EmailOutbox] ([PaymentRequestId]) WHERE [PaymentRequestId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925183344_AddEmailOutbox'
)
BEGIN
    CREATE INDEX [IX_EmailOutbox_Status_NextAttemptAtUtc_LockedUntilUtc] ON [dbo].[EmailOutbox] ([Status], [NextAttemptAtUtc], [LockedUntilUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925183344_AddEmailOutbox'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925183344_AddEmailOutbox', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925192032_AddMachineRequestPreparationStatus'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [PreparationStatus] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925192032_AddMachineRequestPreparationStatus'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [ReadyAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925192032_AddMachineRequestPreparationStatus'
)
BEGIN
    ALTER TABLE [dbo].[MachineRequestPayments] ADD [ReadyByUserId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925192032_AddMachineRequestPreparationStatus'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_PreparationStatus] CHECK ([PreparationStatus] >= 0 AND [PreparationStatus] <= 1)');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925192032_AddMachineRequestPreparationStatus'
)
BEGIN
    EXEC(N'ALTER TABLE [dbo].[MachineRequestPayments] ADD CONSTRAINT [CK_MachineRequestPayments_Ready] CHECK (([PreparationStatus] = 0 AND [ReadyAtUtc] IS NULL AND [ReadyByUserId] IS NULL) OR ([PreparationStatus] = 1 AND [ReadyAtUtc] IS NOT NULL AND [ReadyByUserId] IS NOT NULL))');
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260925192032_AddMachineRequestPreparationStatus'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260925192032_AddMachineRequestPreparationStatus', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002150215_AddConversationMessageVisuals'
)
BEGIN
    CREATE TABLE [chat].[ConversationMessageVisuals] (
        [Id] bigint NOT NULL IDENTITY,
        [ConversationMessageId] bigint NOT NULL,
        [DocumentId] nvarchar(200) NOT NULL,
        [Page] int NOT NULL,
        [AssetType] varchar(4) NOT NULL,
        [Tile] varchar(7) NULL,
        [Name] nvarchar(512) NOT NULL,
        [AssetKey] nvarchar(768) NOT NULL,
        [DisplayOrder] int NOT NULL,
        CONSTRAINT [PK_ConversationMessageVisuals] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ConversationMessageVisuals_AssetType] CHECK ([AssetType] IN ('full', 'tile')),
        CONSTRAINT [CK_ConversationMessageVisuals_DisplayOrder] CHECK ([DisplayOrder] >= 0),
        CONSTRAINT [CK_ConversationMessageVisuals_Page] CHECK ([Page] > 0),
        CONSTRAINT [CK_ConversationMessageVisuals_RequiredStrings] CHECK (LEN([DocumentId]) > 0 AND LEN([Name]) > 0 AND LEN([AssetKey]) > 0),
        CONSTRAINT [FK_ConversationMessageVisuals_ConversationMessages_ConversationMessageId] FOREIGN KEY ([ConversationMessageId]) REFERENCES [chat].[ConversationMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002150215_AddConversationMessageVisuals'
)
BEGIN
    CREATE INDEX [IX_ConversationMessageVisuals_ConversationMessageId] ON [chat].[ConversationMessageVisuals] ([ConversationMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002150215_AddConversationMessageVisuals'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ConversationMessageVisuals_ConversationMessageId_AssetKey] ON [chat].[ConversationMessageVisuals] ([ConversationMessageId], [AssetKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261002150215_AddConversationMessageVisuals'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261002150215_AddConversationMessageVisuals', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[chat].[AiUsageRecords]') AND [c].[name] = N'AgentVersion');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [chat].[AiUsageRecords] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [chat].[AiUsageRecords] DROP COLUMN [AgentVersion];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Machines]') AND [c].[name] = N'FoundryAgentId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Machines] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [dbo].[Machines] DROP COLUMN [FoundryAgentId];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[dbo].[Machines]') AND [c].[name] = N'AgentVersion');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [dbo].[Machines] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [dbo].[Machines] DROP COLUMN [AgentVersion];
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    EXEC sp_rename N'[chat].[Conversations].[FoundryConversationId]', N'ConversationPublicId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    EXEC sp_rename N'[chat].[Conversations].[IX_Conversations_FoundryConversationId]', N'IX_Conversations_ConversationPublicId', 'INDEX';
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    EXEC sp_rename N'[chat].[AiUsageRecords].[FoundryConversationId]', N'ConversationPublicId', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261004201906_FinalClaudeDirectCleanup'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261004201906_FinalClaudeDirectCleanup', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005144223_AddAiUsageCallBreakdownJson'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [CallBreakdownJson] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261005144223_AddAiUsageCallBreakdownJson'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261005144223_AddAiUsageCallBreakdownJson', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [CacheCreation1hInputTokens] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [CacheCreation5mInputTokens] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [CacheCreationInputTokens] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [chat].[AiUsageRecords] ADD [CacheReadInputTokens] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [dbo].[AiPricing] ADD [CacheCreation1hPricePerMillion] decimal(18,8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [dbo].[AiPricing] ADD [CacheCreation5mPricePerMillion] decimal(18,8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    ALTER TABLE [dbo].[AiPricing] ADD [CacheReadPricePerMillion] decimal(18,8) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007104047_AddClaudePromptCacheAccounting'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007104047_AddClaudePromptCacheAccounting', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007145125_AddConversationMessageSourceReferences'
)
BEGIN
    CREATE TABLE [chat].[ConversationMessageSourceReferences] (
        [Id] bigint NOT NULL IDENTITY,
        [ConversationMessageId] bigint NOT NULL,
        [DocumentId] nvarchar(200) NOT NULL,
        [PdfPage] int NOT NULL,
        [DisplayPage] nvarchar(64) NOT NULL,
        [Label] nvarchar(128) NOT NULL,
        [StartIndex] int NOT NULL,
        [EndIndex] int NOT NULL,
        [DisplayOrder] int NOT NULL,
        CONSTRAINT [PK_ConversationMessageSourceReferences] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ConversationMessageSourceReferences_DisplayOrder] CHECK ([DisplayOrder] >= 0),
        CONSTRAINT [CK_ConversationMessageSourceReferences_PdfPage] CHECK ([PdfPage] > 0),
        CONSTRAINT [CK_ConversationMessageSourceReferences_RequiredStrings] CHECK (LEN([DocumentId]) > 0 AND LEN([DisplayPage]) > 0 AND LEN([Label]) > 0),
        CONSTRAINT [CK_ConversationMessageSourceReferences_TextRange] CHECK ([StartIndex] >= 0 AND [EndIndex] > [StartIndex]),
        CONSTRAINT [FK_ConversationMessageSourceReferences_ConversationMessages_ConversationMessageId] FOREIGN KEY ([ConversationMessageId]) REFERENCES [chat].[ConversationMessages] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007145125_AddConversationMessageSourceReferences'
)
BEGIN
    CREATE INDEX [IX_ConversationMessageSourceReferences_ConversationMessageId] ON [chat].[ConversationMessageSourceReferences] ([ConversationMessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007145125_AddConversationMessageSourceReferences'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ConversationMessageSourceReferences_ConversationMessageId_DisplayOrder] ON [chat].[ConversationMessageSourceReferences] ([ConversationMessageId], [DisplayOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [chat].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007145125_AddConversationMessageSourceReferences'
)
BEGIN
    INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007145125_AddConversationMessageSourceReferences', N'10.0.11');
END;

COMMIT;
GO

