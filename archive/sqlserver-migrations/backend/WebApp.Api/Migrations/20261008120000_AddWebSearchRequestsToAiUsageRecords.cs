using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApp.Api.Data;

#nullable disable

namespace WebApp.Api.Migrations;

[DbContext(typeof(DiagLinkDbContext))]
[Migration("20261008120000_AddWebSearchRequestsToAiUsageRecords")]
public partial class AddWebSearchRequestsToAiUsageRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "WebSearchRequests",
            schema: "chat",
            table: "AiUsageRecords",
            type: "int",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "WebSearchRequests",
            schema: "chat",
            table: "AiUsageRecords");
    }
}
