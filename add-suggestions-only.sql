BEGIN TRANSACTION;
ALTER TABLE [chat].[ConversationMessages] ADD [SuggestionsJson] nvarchar(max) NULL;

INSERT INTO [chat].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008140000_AddConversationMessageSuggestions', N'10.0.11');

COMMIT;
GO

