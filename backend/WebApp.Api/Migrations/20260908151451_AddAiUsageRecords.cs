using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageRecords",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsageType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Available = table.Column<bool>(type: "bit", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AssistantMessageId = table.Column<long>(type: "bigint", nullable: true),
                    FoundryConversationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ResponseId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModelSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AgentVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    TotalTokens = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CompanyId_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_MachineId_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "MachineId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UsageType_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "UsageType", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_UserId_CreatedAtUtc",
                schema: "chat",
                table: "AiUsageRecords",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageRecords",
                schema: "chat");
        }
    }
}
