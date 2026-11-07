using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineRequestPaymentAbandonedState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_State",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_Status",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_State",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "([Status] = 0 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 1 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 2 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 3 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL) OR ([Status] = 4 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_Status",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[Status] >= 0 AND [Status] <= 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_State",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_Status",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_State",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "([Status] = 0 AND [StripePaymentIntentId] IS NULL AND [AuthorizationEventId] IS NULL AND [AuthorizedAtUtc] IS NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 1 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 2 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NOT NULL AND [CancelledAtUtc] IS NULL) OR ([Status] = 3 AND [StripePaymentIntentId] IS NOT NULL AND [AuthorizationEventId] IS NOT NULL AND [AuthorizedAtUtc] IS NOT NULL AND [CapturedAtUtc] IS NULL AND [CancelledAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_Status",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[Status] >= 0 AND [Status] <= 3");
        }
    }
}
