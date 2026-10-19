using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationMessageWebCitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationMessageWebCitations",
                schema: "chat",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Url = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Quote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessageWebCitations", x => x.Id);
                    table.CheckConstraint("CK_ConversationMessageWebCitations_DisplayOrder", "[DisplayOrder] >= 0");
                    table.CheckConstraint("CK_ConversationMessageWebCitations_RequiredStrings", "LEN([Url]) > 0 AND LEN([Label]) > 0");
                    table.ForeignKey(
                        name: "FK_ConversationMessageWebCitations_ConversationMessages_ConversationMessageId",
                        column: x => x.ConversationMessageId,
                        principalSchema: "chat",
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageWebCitations_ConversationMessageId",
                schema: "chat",
                table: "ConversationMessageWebCitations",
                column: "ConversationMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageWebCitations_ConversationMessageId_DisplayOrder",
                schema: "chat",
                table: "ConversationMessageWebCitations",
                columns: new[] { "ConversationMessageId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessageWebCitations_ConversationMessageId_Url",
                schema: "chat",
                table: "ConversationMessageWebCitations",
                columns: new[] { "ConversationMessageId", "Url" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationMessageWebCitations",
                schema: "chat");
        }
    }
}
