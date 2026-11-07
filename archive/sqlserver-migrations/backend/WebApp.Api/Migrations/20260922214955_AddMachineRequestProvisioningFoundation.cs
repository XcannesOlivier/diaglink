using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineRequestProvisioningFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivatedAtUtc",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FinalCaptureAmountCents",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstPeriodEndUtc",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProvisioningCompletedAtUtc",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvisioningStage",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ServiceAmountCents",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "CompanyId",
                filter: "[CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_MachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "MachineId",
                unique: true,
                filter: "[MachineId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_FinalCaptureAmount",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[FinalCaptureAmountCents] IS NULL OR ([FinalCaptureAmountCents] > 0 AND [FinalCaptureAmountCents] <= [AmountCents])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_FirstPeriod",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "([ActivatedAtUtc] IS NULL AND [FirstPeriodEndUtc] IS NULL) OR ([ActivatedAtUtc] IS NOT NULL AND [FirstPeriodEndUtc] IS NOT NULL AND [ActivatedAtUtc] < [FirstPeriodEndUtc])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_ProvisioningCompleted",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "([ProvisioningStage] < 6 AND [ProvisioningCompletedAtUtc] IS NULL) OR ([ProvisioningStage] = 6 AND [ProvisioningCompletedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_ProvisioningStage",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[ProvisioningStage] >= 0 AND [ProvisioningStage] <= 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_ServiceAmount",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[ServiceAmountCents] IS NULL OR ([ServiceAmountCents] >= 0 AND [ServiceAmountCents] <= 1990)");

            migrationBuilder.AddForeignKey(
                name: "FK_MachineRequestPayments_Companies_CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "CompanyId",
                principalSchema: "dbo",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MachineRequestPayments_Machines_MachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "MachineId",
                principalSchema: "dbo",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MachineRequestPayments_Companies_CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_MachineRequestPayments_Machines_MachineId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropIndex(
                name: "IX_MachineRequestPayments_CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropIndex(
                name: "IX_MachineRequestPayments_MachineId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_FinalCaptureAmount",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_FirstPeriod",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_ProvisioningCompleted",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_ProvisioningStage",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_ServiceAmount",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "ActivatedAtUtc",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "FinalCaptureAmountCents",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "FirstPeriodEndUtc",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "MachineId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "ProvisioningCompletedAtUtc",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "ProvisioningStage",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "ServiceAmountCents",
                schema: "dbo",
                table: "MachineRequestPayments");
        }
    }
}
