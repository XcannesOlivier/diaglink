using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditLedgerExternalEventIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CreditLedger_ExternalEventId",
                schema: "dbo",
                table: "CreditLedger");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_ExternalEventId",
                schema: "dbo",
                table: "CreditLedger",
                column: "ExternalEventId",
                unique: true,
                filter: "[ExternalEventId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CreditLedger_ExternalEventId",
                schema: "dbo",
                table: "CreditLedger");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_ExternalEventId",
                schema: "dbo",
                table: "CreditLedger",
                column: "ExternalEventId");
        }
    }
}
