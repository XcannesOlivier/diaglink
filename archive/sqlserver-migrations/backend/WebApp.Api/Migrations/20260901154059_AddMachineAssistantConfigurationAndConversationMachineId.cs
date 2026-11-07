using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineAssistantConfigurationAndConversationMachineId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MachineId",
                schema: "chat",
                table: "Conversations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MachineAssistantConfigurations",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProjectEndpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AgentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AgentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AgentVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VectorStoreId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineAssistantConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineAssistantConfigurations_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "chat",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MachineId",
                schema: "chat",
                table: "Conversations",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineAssistantConfigurations_MachineId",
                schema: "chat",
                table: "MachineAssistantConfigurations",
                column: "MachineId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Machines_MachineId",
                schema: "chat",
                table: "Conversations",
                column: "MachineId",
                principalSchema: "chat",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Machines_MachineId",
                schema: "chat",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "MachineAssistantConfigurations",
                schema: "chat");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_MachineId",
                schema: "chat",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "MachineId",
                schema: "chat",
                table: "Conversations");
        }
    }
}
