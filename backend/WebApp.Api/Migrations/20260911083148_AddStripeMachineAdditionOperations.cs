using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeMachineAdditionOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StripeMachineAdditions",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CycleStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CycleEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StripeSubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StripeSubscriptionItemId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StripePriceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalQuantity = table.Column<long>(type: "bigint", nullable: false),
                    TargetQuantity = table.Column<long>(type: "bigint", nullable: false),
                    AiAmountCents = table.Column<int>(type: "int", nullable: false),
                    ServiceAmountCents = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    MachineBillingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StripeInvoiceId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeMachineAdditions", x => x.Id);
                    table.CheckConstraint("CK_StripeMachineAdditions_Amounts", "[AiAmountCents] = 1000 AND [ServiceAmountCents] >= 0 AND [ServiceAmountCents] <= 1990");
                    table.CheckConstraint("CK_StripeMachineAdditions_Cycle", "[CycleStartUtc] <= [ActivatedAtUtc] AND [ActivatedAtUtc] < [CycleEndUtc]");
                    table.CheckConstraint("CK_StripeMachineAdditions_Quantity", "[OriginalQuantity] > 0 AND [TargetQuantity] = [OriginalQuantity] + 1");
                    table.CheckConstraint("CK_StripeMachineAdditions_Stage", "[Stage] >= 0 AND [Stage] <= 6");
                    table.CheckConstraint("CK_StripeMachineAdditions_Payment", "([Stage] < 4 AND [PaymentReference] IS NULL AND [PaymentConfirmedAtUtc] IS NULL) OR ([Stage] >= 4 AND [PaymentReference] IS NOT NULL AND [PaymentReference] <> '' AND [PaymentConfirmedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_StripeMachineAdditions_Period", "([Stage] < 5 AND [MachineBillingPeriodId] IS NULL) OR ([Stage] >= 5 AND [MachineBillingPeriodId] IS NOT NULL)");
                    table.CheckConstraint("CK_StripeMachineAdditions_Completed", "([Stage] < 6 AND [CompletedAtUtc] IS NULL) OR ([Stage] = 6 AND [CompletedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_StripeMachineAdditions_Invoice", "[Stage] < 2 OR [StripeInvoiceId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_BillingAccounts_BillingAccountId",
                        column: x => x.BillingAccountId,
                        principalSchema: "dbo",
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_MachineBillingPeriods_MachineBillingPeriodId",
                        column: x => x.MachineBillingPeriodId,
                        principalSchema: "dbo",
                        principalTable: "MachineBillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeMachineAdditions_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_BillingAccountId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "BillingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_CompanyId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "CompanyId",
                unique: true,
                filter: "[CompletedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_MachineBillingPeriodId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "MachineBillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_MachineId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "MachineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_StripeInvoiceId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "StripeInvoiceId",
                unique: true,
                filter: "[StripeInvoiceId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeMachineAdditions",
                schema: "dbo");
        }
    }
}
