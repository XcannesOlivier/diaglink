using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineRequestPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MachineRequestPayments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EstimatedTotalPages = table.Column<int>(type: "int", nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    StripeSessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AuthorizationEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MachineRequestId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestLinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineRequestPayments", x => x.Id);
                    table.CheckConstraint("CK_MachineRequestPayments_Amount", "[AmountCents] > 0");
                    table.CheckConstraint("CK_MachineRequestPayments_Currency", "[Currency] = 'EUR'");
                    table.CheckConstraint("CK_MachineRequestPayments_Link", "([MachineRequestId] IS NULL AND [RequestLinkedAtUtc] IS NULL) OR ([MachineRequestId] IS NOT NULL AND [RequestLinkedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_MachineRequestPayments_Pages", "[EstimatedTotalPages] > 0");
                    table.CheckConstraint("CK_MachineRequestPayments_State", "([Status] = 0 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 1 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 2 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 3 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_MachineRequestPayments_Status", "[Status] >= 0 AND [Status] <= 3");
                    table.CheckConstraint("CK_MachineRequestPayments_Timestamps", "[UpdatedAtUtc] >= [CreatedAtUtc]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_AuthorizationEventId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "AuthorizationEventId",
                unique: true,
                filter: "[AuthorizationEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_MachineRequestId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "MachineRequestId",
                unique: true,
                filter: "[MachineRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_StripePaymentIntentId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "[StripePaymentIntentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_StripeSessionId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "StripeSessionId",
                unique: true,
                filter: "[StripeSessionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineRequestPayments",
                schema: "dbo");
        }
    }
}
