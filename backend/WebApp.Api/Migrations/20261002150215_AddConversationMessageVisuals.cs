using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessageVisuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationMessageVisuals",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    AssetType = table.Column<string>(type: "varchar(4)", unicode: false, maxLength: 4, nullable: false),
                    Tile = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    AssetKey = table.Column<string>(type: "nvarchar(768)", maxLength: 768, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessageVisuals", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessageVisuals_AssetType", "[AssetType] IN ('full', 'tile')");
                    table.CheckConstraint("CK_ConversationMessageVisuals_DisplayOrder", "[DisplayOrder] >= 0");
                    table.CheckConstraint("CK_ConversationMessageVisuals_Page", "[Page] > 0");
                    table.CheckConstraint("CK_ConversationMessageVisuals_RequiredStrings", "LEN([DocumentId]) > 0 AND LEN([Name]) > 0 AND LEN([AssetKey]) > 0");
                    table.ForeignKey(
                        name: "FK_ConversationMessageVisuals_ConversationMessages_ConversationMessageId",
                        column: x => x.ConversationMessageId,
                        principalSchema: "chat",
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageVisuals_ConversationMessageId",
                schema: "chat",
                table: "ConversationMessageVisuals",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageVisuals_ConversationMessageId_AssetKey",
                schema: "chat",
                table: "ConversationMessageVisuals",
                columns: new[] { "ConversationMessageId", "AssetKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMessageVisuals",
                schema: "chat");
        }
    }
}
