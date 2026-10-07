using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class FinalClaudeDirectCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentVersion",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "FoundryAgentId",
                schema: "dbo",
                table: "Machines");

            migrationBuilder.DropColumn(
                name: "AgentVersion",
                schema: "dbo",
                table: "Machines");

            migrationBuilder.RenameColumn(
                name: "FoundryConversationId",
                schema: "chat",
                table: "Conversations",
                newName: "ConversationPublicId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_FoundryConversationId",
                schema: "chat",
                table: "Conversations",
                newName: "IX_Conversations_ConversationPublicId");

            migrationBuilder.RenameColumn(
                name: "FoundryConversationId",
                schema: "chat",
                table: "AiUsageRecords",
                newName: "ConversationPublicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ConversationPublicId",
                schema: "chat",
                table: "AiUsageRecords",
                newName: "FoundryConversationId");

            migrationBuilder.RenameIndex(
                name: "IX_Conversations_ConversationPublicId",
                schema: "chat",
                table: "Conversations",
                newName: "IX_Conversations_FoundryConversationId");

            migrationBuilder.RenameColumn(
                name: "ConversationPublicId",
                schema: "chat",
                table: "Conversations",
                newName: "FoundryConversationId");

            migrationBuilder.AddColumn<string>(
                name: "AgentVersion",
                schema: "chat",
                table: "AiUsageRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FoundryAgentId",
                schema: "dbo",
                table: "Machines",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentVersion",
                schema: "dbo",
                table: "Machines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
