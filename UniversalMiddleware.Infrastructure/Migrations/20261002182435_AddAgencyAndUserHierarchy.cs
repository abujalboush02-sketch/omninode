using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniversalMiddleware.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgencyAndUserHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tenants",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "AgencyId",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AiTasksQuota",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AiTasksUsedThisMonth",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BillingStatus",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRenewalDate",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TenantType",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "SchemaJson",
                table: "SchemaTemplates",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "RawEvents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Payload",
                table: "RawEvents",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "SourceSchema",
                table: "Mappings",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "FieldMappings",
                table: "Mappings",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "DestinationSchema",
                table: "Mappings",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Payload",
                table: "IncomingEvents",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "ApiKey",
                table: "Connections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AuthHeaderName",
                table: "Connections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthType",
                table: "Connections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "Connections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Agencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OwnerEmail = table.Column<string>(type: "text", nullable: false),
                    SubscriptionTier = table.Column<string>(type: "text", nullable: false),
                    MaxSubTenants = table.Column<int>(type: "integer", nullable: false),
                    MonthlyTaskQuota = table.Column<int>(type: "integer", nullable: false),
                    MonthlyAiQuota = table.Column<int>(type: "integer", nullable: false),
                    TasksUsedThisMonth = table.Column<int>(type: "integer", nullable: false),
                    AiTasksUsedThisMonth = table.Column<int>(type: "integer", nullable: false),
                    BillingStatus = table.Column<string>(type: "text", nullable: false),
                    NextRenewalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FieldMappingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    TargetKey = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Math = table.Column<string>(type: "text", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldMappingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldMappingRules_Endpoints_EndpointId",
                        column: x => x.EndpointId,
                        principalTable: "Endpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountType = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    ReferenceCode = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedByAdminId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsageLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingCycle = table.Column<string>(type: "text", nullable: false),
                    StandardTasksUsed = table.Column<int>(type: "integer", nullable: false),
                    AiTasksUsed = table.Column<int>(type: "integer", nullable: false),
                    OverageIncurred = table.Column<decimal>(type: "numeric", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    AgencyId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Users_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_AgencyId",
                table: "Tenants",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ApiKeyHash",
                table: "Tenants",
                column: "ApiKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantCredentials_EndpointId",
                table: "TenantCredentials",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantCredentials_TenantId",
                table: "TenantCredentials",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RawEvent_WorkerQueue",
                table: "RawEvents",
                columns: new[] { "Status", "LastAttemptAt", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RawEvents_EndpointId",
                table: "RawEvents",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_Endpoints_TenantId_Id",
                table: "Endpoints",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_SourceEndpointId",
                table: "Connections",
                column: "SourceEndpointId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldMappingRules_EndpointId",
                table: "FieldMappingRules",
                column: "EndpointId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_ReferenceCode",
                table: "PaymentInvoices",
                column: "ReferenceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_AgencyId",
                table: "Users",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_Endpoints_SourceEndpointId",
                table: "Connections",
                column: "SourceEndpointId",
                principalTable: "Endpoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Endpoints_Tenants_TenantId",
                table: "Endpoints",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RawEvents_Endpoints_EndpointId",
                table: "RawEvents",
                column: "EndpointId",
                principalTable: "Endpoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantCredentials_Endpoints_EndpointId",
                table: "TenantCredentials",
                column: "EndpointId",
                principalTable: "Endpoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantCredentials_Tenants_TenantId",
                table: "TenantCredentials",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Agencies_AgencyId",
                table: "Tenants",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Connections_Endpoints_SourceEndpointId",
                table: "Connections");

            migrationBuilder.DropForeignKey(
                name: "FK_Endpoints_Tenants_TenantId",
                table: "Endpoints");

            migrationBuilder.DropForeignKey(
                name: "FK_RawEvents_Endpoints_EndpointId",
                table: "RawEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantCredentials_Endpoints_EndpointId",
                table: "TenantCredentials");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantCredentials_Tenants_TenantId",
                table: "TenantCredentials");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Agencies_AgencyId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "FieldMappingRules");

            migrationBuilder.DropTable(
                name: "PaymentInvoices");

            migrationBuilder.DropTable(
                name: "UsageLedgers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Agencies");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_AgencyId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_ApiKeyHash",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_TenantCredentials_EndpointId",
                table: "TenantCredentials");

            migrationBuilder.DropIndex(
                name: "IX_TenantCredentials_TenantId",
                table: "TenantCredentials");

            migrationBuilder.DropIndex(
                name: "IX_RawEvent_WorkerQueue",
                table: "RawEvents");

            migrationBuilder.DropIndex(
                name: "IX_RawEvents_EndpointId",
                table: "RawEvents");

            migrationBuilder.DropIndex(
                name: "IX_Endpoints_TenantId_Id",
                table: "Endpoints");

            migrationBuilder.DropIndex(
                name: "IX_Connections_SourceEndpointId",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "AgencyId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiTasksQuota",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AiTasksUsedThisMonth",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BillingStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "NextRenewalDate",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantType",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApiKey",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "AuthHeaderName",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "AuthType",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "Connections");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tenants",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "SchemaJson",
                table: "SchemaTemplates",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "RawEvents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Payload",
                table: "RawEvents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "SourceSchema",
                table: "Mappings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "FieldMappings",
                table: "Mappings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "DestinationSchema",
                table: "Mappings",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "Payload",
                table: "IncomingEvents",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");
        }
    }
}
