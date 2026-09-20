using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingAndCreditFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTable(
                name: "AiPricing",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UsageType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    InputPricePerMillion = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    OutputPricePerMillion = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPricing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingAccounts",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SubscriptionStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingAccounts_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompanyWallets",
                schema: "dbo",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyWallets", x => x.CompanyId);
                    table.ForeignKey(
                        name: "FK_CompanyWallets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MachineBillingPeriods",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IncludedAiBudgetRealCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    IncludedAiUsedRealCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineBillingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineBillingPeriods_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditLedger",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MachineBillingPeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AiUsageRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BucketType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RealAiCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CommercialCreditAmount = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    ExternalEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditLedger_AiUsageRecords_AiUsageRecordId",
                        column: x => x.AiUsageRecordId,
                        principalSchema: "chat",
                        principalTable: "AiUsageRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLedger_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLedger_MachineBillingPeriods_MachineBillingPeriodId",
                        column: x => x.MachineBillingPeriodId,
                        principalSchema: "dbo",
                        principalTable: "MachineBillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLedger_Machines_MachineId",
                        column: x => x.MachineId,
                        principalSchema: "dbo",
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPricing_Provider_Model_UsageType_EffectiveFromUtc",
                schema: "dbo",
                table: "AiPricing",
                columns: new[] { "Provider", "Model", "UsageType", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_CompanyId",
                schema: "dbo",
                table: "BillingAccounts",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_StripeCustomerId",
                schema: "dbo",
                table: "BillingAccounts",
                column: "StripeCustomerId",
                unique: true,
                filter: "[StripeCustomerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_StripeSubscriptionId",
                schema: "dbo",
                table: "BillingAccounts",
                column: "StripeSubscriptionId",
                unique: true,
                filter: "[StripeSubscriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_AiUsageRecordId",
                schema: "dbo",
                table: "CreditLedger",
                column: "AiUsageRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_CompanyId_CreatedAtUtc",
                schema: "dbo",
                table: "CreditLedger",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_ExternalEventId",
                schema: "dbo",
                table: "CreditLedger",
                column: "ExternalEventId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_MachineBillingPeriodId",
                schema: "dbo",
                table: "CreditLedger",
                column: "MachineBillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLedger_MachineId_CreatedAtUtc",
                schema: "dbo",
                table: "CreditLedger",
                columns: new[] { "MachineId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MachineBillingPeriods_MachineId",
                schema: "dbo",
                table: "MachineBillingPeriods",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineBillingPeriods_MachineId_PeriodStartUtc",
                schema: "dbo",
                table: "MachineBillingPeriods",
                columns: new[] { "MachineId", "PeriodStartUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiPricing",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "BillingAccounts",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CompanyWallets",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "CreditLedger",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "MachineBillingPeriods",
                schema: "dbo");
        }
    }
}
