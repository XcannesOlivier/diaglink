using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClaudePromptCacheAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CacheCreation1hInputTokens",
                schema: "chat",
                table: "AiUsageRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheCreation5mInputTokens",
                schema: "chat",
                table: "AiUsageRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheCreationInputTokens",
                schema: "chat",
                table: "AiUsageRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadInputTokens",
                schema: "chat",
                table: "AiUsageRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CacheCreation1hPricePerMillion",
                schema: "dbo",
                table: "AiPricing",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CacheCreation5mPricePerMillion",
                schema: "dbo",
                table: "AiPricing",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CacheReadPricePerMillion",
                schema: "dbo",
                table: "AiPricing",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheCreation1hInputTokens",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "CacheCreation5mInputTokens",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "CacheCreationInputTokens",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "CacheReadInputTokens",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "CacheCreation1hPricePerMillion",
                schema: "dbo",
                table: "AiPricing");

            migrationBuilder.DropColumn(
                name: "CacheCreation5mPricePerMillion",
                schema: "dbo",
                table: "AiPricing");

            migrationBuilder.DropColumn(
                name: "CacheReadPricePerMillion",
                schema: "dbo",
                table: "AiPricing");
        }
    }
}
