using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniversalMiddleware.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalBillingReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingProvider",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalBillingReference",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingProvider",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExternalBillingReference",
                table: "Tenants");
        }
    }
}
