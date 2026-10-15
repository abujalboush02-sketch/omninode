using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniversalMiddleware.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgenciesPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WhiteLabelAddOn",
                table: "PaymentInvoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BrandColor",
                table: "Agencies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogoDataUrl",
                table: "Agencies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandName",
                table: "Agencies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportFooter",
                table: "Agencies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhiteLabel",
                table: "Agencies",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WhiteLabelAddOn",
                table: "PaymentInvoices");

            migrationBuilder.DropColumn(
                name: "BrandColor",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "BrandLogoDataUrl",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "BrandName",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "ReportFooter",
                table: "Agencies");

            migrationBuilder.DropColumn(
                name: "WhiteLabel",
                table: "Agencies");
        }
    }
}
