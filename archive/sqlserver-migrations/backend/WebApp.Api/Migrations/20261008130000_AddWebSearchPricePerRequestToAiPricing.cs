using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApp.Api.Data;

#nullable disable

namespace WebApp.Api.Migrations;

[DbContext(typeof(DiagLinkDbContext))]
[Migration("20261008130000_AddWebSearchPricePerRequestToAiPricing")]
public partial class AddWebSearchPricePerRequestToAiPricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "WebSearchPricePerRequest",
            schema: "dbo",
            table: "AiPricing",
            type: "decimal(18,8)",
            precision: 18,
            scale: 8,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "WebSearchPricePerRequest",
            schema: "dbo",
            table: "AiPricing");
    }
}
