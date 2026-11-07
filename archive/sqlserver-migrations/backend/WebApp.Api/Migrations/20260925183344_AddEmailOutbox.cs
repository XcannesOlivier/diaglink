using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailOutbox",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MachineRequestId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    PaymentRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NotificationType = table.Column<int>(type: "int", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProviderOperationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailOutbox", x => x.Id);
                    table.CheckConstraint("CK_EmailOutbox_AttemptCount", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_EmailOutbox_Lease", "([LeaseId] IS NULL AND [LockedUntilUtc] IS NULL) OR ([LeaseId] IS NOT NULL AND [LockedUntilUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_EmailOutbox_NotificationType", "[NotificationType] >= 0 AND [NotificationType] <= 4");
                    table.CheckConstraint("CK_EmailOutbox_Sent", "([Status] = 0 AND [SentAtUtc] IS NULL) OR ([Status] = 1 AND [SentAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_EmailOutbox_Status", "[Status] >= 0 AND [Status] <= 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_MachineRequestId_NotificationType",
                schema: "dbo",
                table: "EmailOutbox",
                columns: new[] { "MachineRequestId", "NotificationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_PaymentRequestId",
                schema: "dbo",
                table: "EmailOutbox",
                column: "PaymentRequestId",
                filter: "[PaymentRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_Status_NextAttemptAtUtc_LockedUntilUtc",
                schema: "dbo",
                table: "EmailOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc", "LockedUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailOutbox",
                schema: "dbo");
        }
    }
}
