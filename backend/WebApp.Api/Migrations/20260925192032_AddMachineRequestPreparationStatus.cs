using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineRequestPreparationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreparationStatus",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyAtUtc",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReadyByUserId",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_PreparationStatus",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[PreparationStatus] >= 0 AND [PreparationStatus] <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_Ready",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "([PreparationStatus] = 0 AND [ReadyAtUtc] IS NULL AND [ReadyByUserId] IS NULL) OR ([PreparationStatus] = 1 AND [ReadyAtUtc] IS NOT NULL AND [ReadyByUserId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_PreparationStatus",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_Ready",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "PreparationStatus",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "ReadyAtUtc",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "ReadyByUserId",
                schema: "dbo",
                table: "MachineRequestPayments");
        }
    }
}
