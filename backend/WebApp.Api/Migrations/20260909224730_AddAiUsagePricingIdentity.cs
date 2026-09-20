using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsagePricingIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Deployment",
                schema: "chat",
                table: "AiUsageRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                schema: "chat",
                table: "AiUsageRecords",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Deployment",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "Provider",
                schema: "chat",
                table: "AiUsageRecords");
        }
    }
}
