using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPropertyDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill before the constraint and the unique index exist: each customer's oldest active
            // property (created_at, then id) becomes its primary one.
            migrationBuilder.Sql(
                """
                UPDATE properties p
                SET is_primary = true
                FROM (
                    SELECT DISTINCT ON (customer_id) id
                    FROM properties
                    WHERE is_active
                    ORDER BY customer_id, created_at, id
                ) f
                WHERE p.id = f.id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_properties_primary_active",
                table: "properties",
                sql: "NOT is_primary OR is_active");

            migrationBuilder.CreateIndex(
                name: "ux_properties_customer_primary",
                table: "properties",
                column: "customer_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_properties_organization_id_branch_id",
                table: "properties",
                columns: new[] { "organization_id", "branch_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_properties_branches_organization_id_branch_id",
                table: "properties",
                columns: new[] { "organization_id", "branch_id" },
                principalTable: "branches",
                principalColumns: new[] { "organization_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_notes_customer",
                table: "customer_notes",
                columns: new[] { "organization_id", "customer_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.DropIndex(
                name: "ix_customer_notes_organization_id_customer_id",
                table: "customer_notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_customer_notes_organization_id_customer_id",
                table: "customer_notes",
                columns: new[] { "organization_id", "customer_id" });

            migrationBuilder.DropIndex(
                name: "ix_customer_notes_customer",
                table: "customer_notes");

            migrationBuilder.DropForeignKey(
                name: "fk_properties_branches_organization_id_branch_id",
                table: "properties");

            migrationBuilder.DropIndex(
                name: "ix_properties_organization_id_branch_id",
                table: "properties");

            migrationBuilder.DropIndex(
                name: "ux_properties_customer_primary",
                table: "properties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_properties_primary_active",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "properties");
        }
    }
}
