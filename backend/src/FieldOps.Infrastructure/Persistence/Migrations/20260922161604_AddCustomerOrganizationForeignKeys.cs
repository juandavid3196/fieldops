using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOrganizationForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "fk_customer_contacts_organizations_organization_id",
                table: "customer_contacts",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_notes_organizations_organization_id",
                table: "customer_notes",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_properties_organizations_organization_id",
                table: "properties",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customer_contacts_organizations_organization_id",
                table: "customer_contacts");

            migrationBuilder.DropForeignKey(
                name: "fk_customer_notes_organizations_organization_id",
                table: "customer_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_properties_organizations_organization_id",
                table: "properties");
        }
    }
}
