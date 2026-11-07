using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeWalletTopUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Journal TopUp is append-only, including writes outside EF. No existing row is changed.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_CreditLedger_TopUpImmutable]
                ON [dbo].[CreditLedger] AFTER UPDATE, DELETE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted WHERE [EntryType] = ''TopUp'')
                        THROW 51001, ''TopUp ledger entries are immutable.'', 1;
                END');
                """);
            migrationBuilder.CreateTable(
                name: "StripeWalletTopUps",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    ReturnUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    StripeSessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWalletTopUps", x => x.Id);
                    table.CheckConstraint("CK_StripeWalletTopUps_Amount", "[AmountCents] >= 1000 AND [AmountCents] <= 99999999 AND [Currency] = 'EUR'");
                    table.CheckConstraint("CK_StripeWalletTopUps_Completed", "([Stage] < 5 AND [CompletedAtUtc] IS NULL) OR ([Stage] = 5 AND [CompletedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_StripeWalletTopUps_Ledger", "([Stage] < 4 AND [LedgerEntryId] IS NULL) OR ([Stage] >= 4 AND [LedgerEntryId] IS NOT NULL)");
                    table.CheckConstraint("CK_StripeWalletTopUps_Payment", "([Stage] < 3 AND [StripePaymentIntentId] IS NULL AND [PaymentConfirmedAtUtc] IS NULL) OR ([Stage] >= 3 AND [StripePaymentIntentId] IS NOT NULL AND [PaymentConfirmedAtUtc] IS NOT NULL AND [ExternalEventId] IS NOT NULL)");
                    table.CheckConstraint("CK_StripeWalletTopUps_Session", "[Stage] = 0 OR [StripeSessionId] IS NOT NULL");
                    table.CheckConstraint("CK_StripeWalletTopUps_Stage", "[Stage] >= 0 AND [Stage] <= 5");
                    table.ForeignKey(
                        name: "FK_StripeWalletTopUps_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StripeWalletTopUps_CreditLedger_LedgerEntryId",
                        column: x => x.LedgerEntryId,
                        principalSchema: "dbo",
                        principalTable: "CreditLedger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_CompanyId_CreatedAtUtc",
                schema: "dbo",
                table: "StripeWalletTopUps",
                columns: new[] { "CompanyId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_ExternalEventId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "ExternalEventId",
                unique: true,
                filter: "[ExternalEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_LedgerEntryId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "LedgerEntryId",
                unique: true,
                filter: "[LedgerEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_StripePaymentIntentId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "[StripePaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWalletTopUps_StripeSessionId",
                schema: "dbo",
                table: "StripeWalletTopUps",
                column: "StripeSessionId",
                unique: true,
                filter: "[StripeSessionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_CreditLedger_TopUpImmutable];");
            migrationBuilder.DropTable(
                name: "StripeWalletTopUps",
                schema: "dbo");
        }
    }
}
