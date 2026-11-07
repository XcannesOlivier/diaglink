using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdditionalDocumentsRequestModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.AddColumn<Guid>(
                name: "TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineRequestPayments_TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "TargetMachineId",
                filter: "[TargetMachineId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[RequestKind] >= 0 AND [RequestKind] <= 2");

            migrationBuilder.AddForeignKey(
                name: "FK_MachineRequestPayments_Machines_TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments",
                column: "TargetMachineId",
                principalSchema: "dbo",
                principalTable: "Machines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MachineRequestPayments_Machines_TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropIndex(
                name: "IX_MachineRequestPayments_TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "TargetMachineId",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[RequestKind] >= 0 AND [RequestKind] <= 1");
        }
    }
}
