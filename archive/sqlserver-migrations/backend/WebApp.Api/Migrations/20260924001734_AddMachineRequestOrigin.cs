using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineRequestOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                schema: "dbo",
                table: "MachineRequestPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MachineRequestPayments_RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments",
                sql: "[RequestKind] >= 0 AND [RequestKind] <= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MachineRequestPayments_RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "RequestKind",
                schema: "dbo",
                table: "MachineRequestPayments");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                schema: "dbo",
                table: "MachineRequestPayments");
        }
    }
}
