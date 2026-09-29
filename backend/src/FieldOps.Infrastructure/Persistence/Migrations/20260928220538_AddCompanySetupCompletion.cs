using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySetupCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_line1",
                table: "organizations",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country_code",
                table: "organizations",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "organizations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "prices_include_tax",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "state_region",
                table: "organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "organizations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_main",
                table: "branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "service_postal_codes",
                table: "branches",
                type: "varchar(30)[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<bool>(
                name: "uses_company_billing",
                table: "branches",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // BR-14 backfill: the oldest active branch by (created_at, id) of
            // each organization becomes its main branch. Runs before the
            // unique index and the check constraint below. An organization
            // without an active branch aborts the migration; branch status is
            // never changed and updated_at is not bumped.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM organizations o
                        WHERE NOT EXISTS (
                            SELECT 1 FROM branches b
                            WHERE b.organization_id = o.id AND b.is_active)) THEN
                        RAISE EXCEPTION 'Cannot backfill the main branch: an organization has no active branch.';
                    END IF;

                    UPDATE branches
                    SET is_main = true
                    WHERE id IN (
                        SELECT DISTINCT ON (organization_id) id
                        FROM branches
                        WHERE is_active
                        ORDER BY organization_id, created_at, id);
                END
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "organization_logos",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_logos", x => x.organization_id);
                    table.CheckConstraint("ck_organization_logos_content_type", "content_type IN ('image/png','image/jpeg','image/svg+xml')");
                    table.CheckConstraint("ck_organization_logos_size_bytes", "size_bytes BETWEEN 1 AND 2097152");
                    table.ForeignKey(
                        name: "fk_organization_logos_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_branches_org_main",
                table: "branches",
                column: "organization_id",
                unique: true,
                filter: "is_main");

            migrationBuilder.AddCheckConstraint(
                name: "ck_branches_main_active",
                table: "branches",
                sql: "NOT is_main OR is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organization_logos");

            migrationBuilder.DropIndex(
                name: "ux_branches_org_main",
                table: "branches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_branches_main_active",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "address_line1",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "city",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "country_code",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "prices_include_tax",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "state_region",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "website",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "is_main",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "service_postal_codes",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "uses_company_billing",
                table: "branches");
        }
    }
}
