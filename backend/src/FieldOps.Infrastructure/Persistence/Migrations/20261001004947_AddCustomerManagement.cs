using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // branch_id is added nullable, backfilled with each organization's main branch, then made
            // NOT NULL. An organization with customers and no main branch fails this migration loudly.
            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                table: "customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE customers c
                SET branch_id = b.id
                FROM branches b
                WHERE b.organization_id = c.organization_id AND b.is_main;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "branch_id",
                table: "customers",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "prefers_email",
                table: "customer_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "prefers_sms",
                table: "customer_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "customer_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_tags", x => x.id);
                    table.UniqueConstraint("ak_customer_tags_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_customer_tags_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "customer_tag_assignments",
                columns: table => new
                {
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_tag_assignments", x => new { x.customer_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_customer_tag_assignments_customer_tags_organization_id_tag_",
                        columns: x => new { x.organization_id, x.tag_id },
                        principalTable: "customer_tags",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_customer_tag_assignments_customers_organization_id_customer",
                        columns: x => new { x.organization_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_customer_tag_assignments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_branch_id",
                table: "customers",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_org_branch",
                table: "customers",
                columns: new[] { "organization_id", "branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_org_phone",
                table: "customer_contacts",
                columns: new[] { "organization_id", "phone" });

            migrationBuilder.CreateIndex(
                name: "ux_customer_contacts_primary",
                table: "customer_contacts",
                column: "customer_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_contacts_preferred_channel",
                table: "customer_contacts",
                sql: "prefers_email OR prefers_sms");

            migrationBuilder.CreateIndex(
                name: "ix_customer_tag_assignments_org_tag",
                table: "customer_tag_assignments",
                columns: new[] { "organization_id", "tag_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_tag_assignments_organization_id_customer_id",
                table: "customer_tag_assignments",
                columns: new[] { "organization_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ux_customer_tags_org_normalized_name",
                table: "customer_tags",
                columns: new[] { "organization_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_customers_branches_branch_id",
                table: "customers",
                column: "branch_id",
                principalTable: "branches",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_customers_branches_organization_id_branch_id",
                table: "customers",
                columns: new[] { "organization_id", "branch_id" },
                principalTable: "branches",
                principalColumns: new[] { "organization_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customers_branches_branch_id",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "fk_customers_branches_organization_id_branch_id",
                table: "customers");

            migrationBuilder.DropTable(
                name: "customer_tag_assignments");

            migrationBuilder.DropTable(
                name: "customer_tags");

            migrationBuilder.DropIndex(
                name: "ix_customers_branch_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_org_branch",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_contacts_org_phone",
                table: "customer_contacts");

            migrationBuilder.DropIndex(
                name: "ux_customer_contacts_primary",
                table: "customer_contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_contacts_preferred_channel",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "prefers_email",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "prefers_sms",
                table: "customer_contacts");
        }
    }
}
