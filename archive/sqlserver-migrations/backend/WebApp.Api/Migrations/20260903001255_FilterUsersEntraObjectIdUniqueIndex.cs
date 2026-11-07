using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class FilterUsersEntraObjectIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[dbo].[Users]')
                      AND [name] = N'IX_Users_EntraObjectId_NotNull')
                BEGIN
                    DROP INDEX [IX_Users_EntraObjectId_NotNull] ON [dbo].[Users];
                END;

                IF (SELECT COUNT(*) FROM [dbo].[Users] WHERE [EntraObjectId] IS NULL) > 1
                BEGIN
                    THROW 51000, 'Cannot restore unfiltered unique constraint on dbo.Users.EntraObjectId while multiple NULL values exist.', 1;
                END;

                IF EXISTS (
                    SELECT [EntraObjectId]
                    FROM [dbo].[Users]
                    WHERE [EntraObjectId] IS NOT NULL
                    GROUP BY [EntraObjectId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51001, 'Cannot restore unfiltered unique constraint on dbo.Users.EntraObjectId while duplicate non-null values exist.', 1;
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.key_constraints
                    WHERE parent_object_id = OBJECT_ID(N'[dbo].[Users]')
                      AND [name] = N'UQ_Users_EntraObjectId')
                BEGIN
                    ALTER TABLE [dbo].[Users]
                    ADD CONSTRAINT [UQ_Users_EntraObjectId] UNIQUE ([EntraObjectId]);
                END;
                """);
        }
    }
}
