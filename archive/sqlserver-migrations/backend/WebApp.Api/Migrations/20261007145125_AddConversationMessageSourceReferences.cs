using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessageSourceReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationMessageSourceReferences",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PdfPage = table.Column<int>(type: "int", nullable: false),
                    DisplayPage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartIndex = table.Column<int>(type: "int", nullable: false),
                    EndIndex = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessageSourceReferences", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_DisplayOrder", "[DisplayOrder] >= 0");
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_PdfPage", "[PdfPage] > 0");
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_RequiredStrings", "LEN([DocumentId]) > 0 AND LEN([DisplayPage]) > 0 AND LEN([Label]) > 0");
                    table.CheckConstraint("CK_ConversationMessageSourceReferences_TextRange", "[StartIndex] >= 0 AND [EndIndex] > [StartIndex]");
                    table.ForeignKey(
                        name: "FK_ConversationMessageSourceReferences_ConversationMessages_ConversationMessageId",
                        column: x => x.ConversationMessageId,
                        principalSchema: "chat",
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageSourceReferences_ConversationMessageId",
                schema: "chat",
                table: "ConversationMessageSourceReferences",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageSourceReferences_ConversationMessageId_DisplayOrder",
                schema: "chat",
                table: "ConversationMessageSourceReferences",
                columns: new[] { "ConversationMessageId", "DisplayOrder" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMessageSourceReferences",
                schema: "chat");
        }
    }
}
