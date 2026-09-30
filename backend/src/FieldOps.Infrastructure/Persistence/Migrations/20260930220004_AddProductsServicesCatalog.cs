using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductsServicesCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_taxable",
                table: "catalog_items",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "catalog_items",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                computedColumnSql: "lower(name)",
                stored: true);

            migrationBuilder.CreateTable(
                name: "catalog_item_images",
                columns: table => new
                {
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_item_images", x => x.catalog_item_id);
                    table.CheckConstraint("ck_catalog_item_images_content_type", "content_type IN ('image/png','image/jpeg')");
                    table.CheckConstraint("ck_catalog_item_images_size_bytes", "size_bytes BETWEEN 1 AND 5242880");
                    table.ForeignKey(
                        name: "fk_catalog_item_images_catalog_items_organization_id_catalog_i",
                        columns: x => new { x.organization_id, x.catalog_item_id },
                        principalTable: "catalog_items",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_catalog_item_images_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ux_catalog_items_org_type_name",
                table: "catalog_items",
                columns: new[] { "organization_id", "type", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_catalog_item_images_organization_id_catalog_item_id",
                table: "catalog_item_images",
                columns: new[] { "organization_id", "catalog_item_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_item_images");

            migrationBuilder.DropIndex(
                name: "ux_catalog_items_org_type_name",
                table: "catalog_items");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "catalog_items");

            migrationBuilder.DropColumn(
                name: "is_taxable",
                table: "catalog_items");
        }
    }
}
