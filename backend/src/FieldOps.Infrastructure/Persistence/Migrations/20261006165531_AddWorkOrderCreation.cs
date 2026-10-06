using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_branches_branch_id",
                table: "work_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_quote_versions_quote_version_id",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_branch_id",
                table: "work_orders");

            migrationBuilder.AddColumn<int>(
                name: "estimated_duration_minutes",
                table: "work_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_type",
                table: "work_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "one_time");

            migrationBuilder.AddColumn<bool>(
                name: "notify_customer_when_scheduled",
                table: "work_orders",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<short>(
                name: "recurrence_count",
                table: "work_orders",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recurrence_frequency",
                table: "work_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "send_arrival_reminder",
                table: "work_orders",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "send_technician_details",
                table: "work_orders",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_category_id",
                table: "work_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "work_orders",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "checklist_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_templates", x => x.id);
                    table.UniqueConstraint("ak_checklist_templates_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_checklist_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_checklist_templates_service_categories_organization_id_serv",
                        columns: x => new { x.organization_id, x.service_category_id },
                        principalTable: "service_categories",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_checklist_templates_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            // Case-insensitive uniqueness of the template name (create-work-order BR-11). EF cannot model an
            // expression index, so it is created with SQL, like ux_skills_org_normalized_name.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_checklist_templates_org_name ON checklist_templates (organization_id, lower(name));");

            migrationBuilder.CreateTable(
                name: "work_order_planned_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_planned_materials", x => x.id);
                    table.CheckConstraint("ck_work_order_planned_materials_quantity", "quantity > 0");
                    table.CheckConstraint("ck_work_order_planned_materials_source", "source IN ('truck_stock','warehouse','to_purchase')");
                    table.ForeignKey(
                        name: "fk_work_order_planned_materials_catalog_items_organization_id_",
                        columns: x => new { x.organization_id, x.catalog_item_id },
                        principalTable: "catalog_items",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_work_order_planned_materials_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_work_order_planned_materials_quote_lines_quote_line_id",
                        column: x => x.quote_line_id,
                        principalTable: "quote_lines",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_work_order_planned_materials_work_orders_organization_id_wo",
                        columns: x => new { x.organization_id, x.work_order_id },
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checklist_template_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklist_template_items_checklist_templates_organization_i",
                        columns: x => new { x.organization_id, x.template_id },
                        principalTable: "checklist_templates",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_checklist_template_items_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_org_created",
                table: "work_orders",
                columns: new[] { "organization_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_organization_id_quote_version_id",
                table: "work_orders",
                columns: new[] { "organization_id", "quote_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_organization_id_service_category_id",
                table: "work_orders",
                columns: new[] { "organization_id", "service_category_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_estimated_duration_minutes",
                table: "work_orders",
                sql: "estimated_duration_minutes BETWEEN 30 AND 720 AND estimated_duration_minutes % 30 = 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_job_type",
                table: "work_orders",
                sql: "job_type IN ('one_time','recurring')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_preferred_window",
                table: "work_orders",
                sql: "(preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_recurrence_count",
                table: "work_orders",
                sql: "recurrence_count BETWEEN 2 AND 24");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_recurrence_frequency",
                table: "work_orders",
                sql: "recurrence_frequency IN ('weekly','biweekly','monthly','quarterly')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_recurrence_job_type",
                table: "work_orders",
                sql: "(job_type = 'recurring') = (recurrence_frequency IS NOT NULL AND recurrence_count IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_recurrence_pair",
                table: "work_orders",
                sql: "(recurrence_frequency IS NULL) = (recurrence_count IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_template_items_organization_id_template_id",
                table: "checklist_template_items",
                columns: new[] { "organization_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_created_by_user_id",
                table: "checklist_templates",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_checklist_templates_organization_id_service_category_id",
                table: "checklist_templates",
                columns: new[] { "organization_id", "service_category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_planned_materials_organization_id_catalog_item_id",
                table: "work_order_planned_materials",
                columns: new[] { "organization_id", "catalog_item_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_planned_materials_organization_id_work_order_id",
                table: "work_order_planned_materials",
                columns: new[] { "organization_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_planned_materials_quote_line_id",
                table: "work_order_planned_materials",
                column: "quote_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_planned_materials_work_order_id",
                table: "work_order_planned_materials",
                column: "work_order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_branches_organization_id_branch_id",
                table: "work_orders",
                columns: new[] { "organization_id", "branch_id" },
                principalTable: "branches",
                principalColumns: new[] { "organization_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_quote_versions_organization_id_quote_version_id",
                table: "work_orders",
                columns: new[] { "organization_id", "quote_version_id" },
                principalTable: "quote_versions",
                principalColumns: new[] { "organization_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_service_categories_organization_id_service_cate",
                table: "work_orders",
                columns: new[] { "organization_id", "service_category_id" },
                principalTable: "service_categories",
                principalColumns: new[] { "organization_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_branches_organization_id_branch_id",
                table: "work_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_quote_versions_organization_id_quote_version_id",
                table: "work_orders");

            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_service_categories_organization_id_service_cate",
                table: "work_orders");

            migrationBuilder.Sql("DROP INDEX ux_checklist_templates_org_name;");

            migrationBuilder.DropTable(
                name: "checklist_template_items");

            migrationBuilder.DropTable(
                name: "work_order_planned_materials");

            migrationBuilder.DropTable(
                name: "checklist_templates");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_org_created",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_organization_id_quote_version_id",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_organization_id_service_category_id",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_estimated_duration_minutes",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_job_type",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_preferred_window",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_recurrence_count",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_recurrence_frequency",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_recurrence_job_type",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_recurrence_pair",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "estimated_duration_minutes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "job_type",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "notify_customer_when_scheduled",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "recurrence_count",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "recurrence_frequency",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "send_arrival_reminder",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "send_technician_details",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "service_category_id",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "title",
                table: "work_orders");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_branch_id",
                table: "work_orders",
                column: "branch_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_branches_branch_id",
                table: "work_orders",
                column: "branch_id",
                principalTable: "branches",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_quote_versions_quote_version_id",
                table: "work_orders",
                column: "quote_version_id",
                principalTable: "quote_versions",
                principalColumn: "id");
        }
    }
}
