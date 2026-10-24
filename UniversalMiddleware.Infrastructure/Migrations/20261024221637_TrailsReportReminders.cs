using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniversalMiddleware.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TrailsReportReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHint",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAi",
                table: "RawEvents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentReportedAt",
                table: "PaymentInvoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReminderLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountType = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    CycleKey = table.Column<string>(type: "text", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_TenantId_ReceivedAt",
                table: "RawEvents",
                columns: new[] { "TenantId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderLogs_AccountId_Kind_CycleKey",
                table: "ReminderLogs",
                columns: new[] { "AccountId", "Kind", "CycleKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderLogs");

            migrationBuilder.DropIndex(
                name: "IX_RawEvents_TenantId_ReceivedAt",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "ApiKeyHint",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsAi",
                table: "RawEvents");

            migrationBuilder.DropColumn(
                name: "PaymentReportedAt",
                table: "PaymentInvoices");
        }
    }
}
