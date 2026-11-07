using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyWalletLedgerIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_CreditLedger_CompanyWalletUsage",
                schema: "dbo",
                table: "CreditLedger",
                column: "AiUsageRecordId",
                unique: true,
                filter: "[AiUsageRecordId] IS NOT NULL AND [BucketType] = 'CompanyWallet' AND [EntryType] = 'AiUsage'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CreditLedger_CompanyWalletUsage",
                schema: "dbo",
                table: "CreditLedger");
        }
    }
}
