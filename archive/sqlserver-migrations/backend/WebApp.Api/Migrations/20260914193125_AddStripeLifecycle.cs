using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StripeMachineAdditions_MachineId",
                schema: "dbo",
                table: "StripeMachineAdditions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StripeMachineAdditions_Quantity",
                schema: "dbo",
                table: "StripeMachineAdditions");

            migrationBuilder.AddColumn<long>(
                name: "AmountRemainingCents",
                schema: "dbo",
                table: "BillingAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancelAtPeriodEnd",
                schema: "dbo",
                table: "BillingAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LatestInvoiceId",
                schema: "dbo",
                table: "BillingAccounts",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LatestInvoiceStatus",
                schema: "dbo",
                table: "BillingAccounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StripeLifecycleEvents",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MachineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeLifecycleEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StripeLifecycleEvents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_MachineId_CycleStartUtc",
                schema: "dbo",
                table: "StripeMachineAdditions",
                columns: new[] { "MachineId", "CycleStartUtc" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StripeMachineAdditions_Quantity",
                schema: "dbo",
                table: "StripeMachineAdditions",
                sql: "[OriginalQuantity] >= 0 AND [TargetQuantity] = [OriginalQuantity] + 1");

            migrationBuilder.CreateIndex(
                name: "IX_StripeLifecycleEvents_CompanyId",
                schema: "dbo",
                table: "StripeLifecycleEvents",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeLifecycleEvents",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_StripeMachineAdditions_MachineId_CycleStartUtc",
                schema: "dbo",
                table: "StripeMachineAdditions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StripeMachineAdditions_Quantity",
                schema: "dbo",
                table: "StripeMachineAdditions");

            migrationBuilder.DropColumn(
                name: "AmountRemainingCents",
                schema: "dbo",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "CancelAtPeriodEnd",
                schema: "dbo",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "LatestInvoiceId",
                schema: "dbo",
                table: "BillingAccounts");

            migrationBuilder.DropColumn(
                name: "LatestInvoiceStatus",
                schema: "dbo",
                table: "BillingAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_StripeMachineAdditions_MachineId",
                schema: "dbo",
                table: "StripeMachineAdditions",
                column: "MachineId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StripeMachineAdditions_Quantity",
                schema: "dbo",
                table: "StripeMachineAdditions",
                sql: "[OriginalQuantity] > 0 AND [TargetQuantity] = [OriginalQuantity] + 1");
        }
    }
}
