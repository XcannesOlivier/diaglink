using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApp.Api.Data;

#nullable disable

namespace WebApp.Api.Migrations;

[DbContext(typeof(DiagLinkDbContext))]
[Migration("20261008140000_AddConversationMessageSuggestions")]
public partial class AddConversationMessageSuggestions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SuggestionsJson",
            schema: DiagLinkDbContext.Schema,
            table: "ConversationMessages",
            type: "nvarchar(max)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SuggestionsJson",
            schema: DiagLinkDbContext.Schema,
            table: "ConversationMessages");
    }
}
