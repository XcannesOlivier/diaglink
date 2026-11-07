using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisionUsageCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallId",
                schema: "chat",
                table: "AiUsageRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentResponseId",
                schema: "chat",
                table: "AiUsageRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CallId",
                schema: "chat",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "ParentResponseId",
                schema: "chat",
                table: "AiUsageRecords");
        }
    }
}
