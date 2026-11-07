using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeMachineAdditionPaymentEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalEventId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_ExternalEventId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "ExternalEventId",
                unique: true,
                filter: "[ExternalEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StripeMachineAdditions_ExternalEventId",
                schema: "dbo",
                table: "StripeMachineAdditions");

            migrationBuilder.DropColumn(
                name: "ExternalEventId",
                schema: "dbo",
                table: "StripeMachineAdditions");
        }
    }
}
