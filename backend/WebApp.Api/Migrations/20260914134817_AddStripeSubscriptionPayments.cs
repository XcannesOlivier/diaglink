using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeSubscriptionPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StripeSubscriptionPayments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StripeInvoiceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillingReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AmountPaidCents = table.Column<long>(type: "bigint", nullable: false),
                    PaymentReference = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    MachineIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeSubscriptionPayments", x => x.Id);
                    table.CheckConstraint("CK_StripeSubscriptionPayments_Amount", "[AmountPaidCents] > 0");
                    table.CheckConstraint("CK_StripeSubscriptionPayments_Cycle", "[PeriodEndUtc] > [PeriodStartUtc]");
                    table.CheckConstraint("CK_StripeSubscriptionPayments_Reason", "[BillingReason] IN ('subscription_create', 'subscription_cycle')");
                    table.ForeignKey(
                        name: "FK_StripeSubscriptionPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_CompanyId",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_ExternalEventId",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                column: "ExternalEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_StripeInvoiceId",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                column: "StripeInvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeSubscriptionPayments_StripeSubscriptionId_PeriodStartUtc",
                schema: "dbo",
                table: "StripeSubscriptionPayments",
                columns: new[] { "StripeSubscriptionId", "PeriodStartUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeSubscriptionPayments",
                schema: "dbo");
        }
    }
}
