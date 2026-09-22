using System;
using FieldOps.Domain.WorkOrders;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrdersAndVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:message_visibility", "customer,internal")
                .Annotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .Annotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .Annotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .Annotation("Npgsql:Enum:visit_status", "unscheduled,scheduled,assigned,on_the_way,in_progress,paused,completed,needs_correction,approved,cancelled")
                .Annotation("Npgsql:Enum:work_order_status", "draft,ready_to_schedule,scheduled,in_progress,completed,approved_for_billing,cancelled")
                .OldAnnotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .OldAnnotation("Npgsql:Enum:catalog_item_type", "service,product")
                .OldAnnotation("Npgsql:Enum:customer_type", "person,company")
                .OldAnnotation("Npgsql:Enum:message_visibility", "customer,internal")
                .OldAnnotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled");

            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_number = table.Column<long>(type: "bigint", nullable: false),
                    quote_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<WorkOrderStatus>(type: "work_order_status", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3),
                    scope_snapshot = table.Column<string>(type: "text", nullable: false),
                    internal_instructions = table.Column<string>(type: "text", nullable: true),
                    preferred_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preferred_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.UniqueConstraint("ak_work_orders_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_work_orders_priority", "priority BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_work_orders_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_work_orders_customers_organization_id_customer_id",
                        columns: x => new { x.organization_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_work_orders_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_work_orders_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_work_orders_quote_versions_quote_version_id",
                        column: x => x.quote_version_id,
                        principalTable: "quote_versions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_work_orders_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<VisitStatus>(type: "visit_status", nullable: false),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pause_seconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    completion_summary = table.Column<string>(type: "text", nullable: true),
                    completion_without_signature_reason = table.Column<string>(type: "text", nullable: true),
                    review_notes = table.Column<string>(type: "text", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                    table.UniqueConstraint("ak_visits_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_visits_pause_seconds", "pause_seconds >= 0");
                    table.CheckConstraint("ck_visits_schedule_range", "scheduled_end IS NULL OR scheduled_start IS NULL OR scheduled_start < scheduled_end");
                    table.CheckConstraint("ck_visits_visit_number", "visit_number > 0");
                    table.ForeignKey(
                        name: "fk_visits_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visits_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visits_work_orders_organization_id_work_order_id",
                        columns: x => new { x.organization_id, x.work_order_id },
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "work_order_checklist_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_checklist_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_order_checklist_templates_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_required_skills",
                columns: table => new
                {
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minimum_proficiency = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_required_skills", x => new { x.work_order_id, x.skill_id });
                    table.CheckConstraint("ck_work_order_required_skills_minimum_proficiency", "minimum_proficiency BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_work_order_required_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_work_order_required_skills_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_signoffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signer_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    signer_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    signature_storage_key = table.Column<string>(type: "text", nullable: true),
                    accepted = table.Column<bool>(type: "boolean", nullable: false),
                    comments = table.Column<string>(type: "text", nullable: true),
                    absence_reason = table.Column<string>(type: "text", nullable: true),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_signoffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_signoffs_customer_contacts_signer_contact_id",
                        column: x => x.signer_contact_id,
                        principalTable: "customer_contacts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_customer_signoffs_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "visit_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    unassigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_assignments_technician_profiles_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technician_profiles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_assignments_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    caption = table.Column<string>(type: "text", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_evidence", x => x.id);
                    table.CheckConstraint("ck_visit_evidence_evidence_type", "evidence_type IN ('before','during','after','incident','other')");
                    table.CheckConstraint("ck_visit_evidence_size_bytes", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_visit_evidence_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_evidence_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_incidents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    additional_work_requested = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_incidents", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_incidents_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_incidents_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, defaultValue: 0m),
                    billable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_materials", x => x.id);
                    table.CheckConstraint("ck_visit_materials_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_visit_materials_catalog_items_catalog_item_id",
                        column: x => x.catalog_item_id,
                        principalTable: "catalog_items",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_materials_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<VisitStatus>(type: "visit_status", nullable: true),
                    to_status = table.Column<VisitStatus>(type: "visit_status", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_status_history_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_status_history_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_time_entries", x => x.id);
                    table.CheckConstraint("ck_visit_time_entries_entry_type", "entry_type IN ('work','pause','travel')");
                    table.CheckConstraint("ck_visit_time_entries_start_end", "ended_at IS NULL OR started_at < ended_at");
                    table.ForeignKey(
                        name: "fk_visit_time_entries_technician_profiles_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technician_profiles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_time_entries_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    label = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_checklist_items_users_completed_by_user_id",
                        column: x => x.completed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_checklist_items_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_visit_checklist_items_work_order_checklist_templates_templa",
                        column: x => x.template_item_id,
                        principalTable: "work_order_checklist_templates",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_signoffs_signer_contact_id",
                table: "customer_signoffs",
                column: "signer_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_signoffs_visit_id",
                table: "customer_signoffs",
                column: "visit_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assignments_technician",
                table: "visit_assignments",
                columns: new[] { "technician_id", "assigned_at" },
                filter: "unassigned_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visit_assignments_active_technician_unique",
                table: "visit_assignments",
                columns: new[] { "visit_id", "technician_id" },
                unique: true,
                filter: "unassigned_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visit_assignments_assigned_by_user_id",
                table: "visit_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_assignments_visit_id_technician_id_unassigned_at",
                table: "visit_assignments",
                columns: new[] { "visit_id", "technician_id", "unassigned_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_checklist_items_completed_by_user_id",
                table: "visit_checklist_items",
                column: "completed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_checklist_items_template_item_id",
                table: "visit_checklist_items",
                column: "template_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_checklist_items_visit_id",
                table: "visit_checklist_items",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_evidence_uploaded_by_user_id",
                table: "visit_evidence",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_evidence_visit_id",
                table: "visit_evidence",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_incidents_created_by_user_id",
                table: "visit_incidents",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_incidents_visit_id",
                table: "visit_incidents",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_materials_catalog_item_id",
                table: "visit_materials",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_materials_visit_id",
                table: "visit_materials",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_status_history_changed_by_user_id",
                table: "visit_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_status_history_visit_id",
                table: "visit_status_history",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_time_entries_technician_id",
                table: "visit_time_entries",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_time_entries_visit_id",
                table: "visit_time_entries",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_organization_id_work_order_id",
                table: "visits",
                columns: new[] { "organization_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_reviewed_by_user_id",
                table: "visits",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_schedule",
                table: "visits",
                columns: new[] { "organization_id", "scheduled_start", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_work_order_id_visit_number",
                table: "visits",
                columns: new[] { "work_order_id", "visit_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_checklist_templates_work_order_id",
                table: "work_order_checklist_templates",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_required_skills_skill_id",
                table: "work_order_required_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_branch_id",
                table: "work_orders",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_created_by_user_id",
                table: "work_orders",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_organization_id_customer_id",
                table: "work_orders",
                columns: new[] { "organization_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_organization_id_property_id",
                table: "work_orders",
                columns: new[] { "organization_id", "property_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_organization_id_work_order_number",
                table: "work_orders",
                columns: new[] { "organization_id", "work_order_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_quote_version_id",
                table: "work_orders",
                column: "quote_version_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_status",
                table: "work_orders",
                columns: new[] { "organization_id", "branch_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_signoffs");

            migrationBuilder.DropTable(
                name: "visit_assignments");

            migrationBuilder.DropTable(
                name: "visit_checklist_items");

            migrationBuilder.DropTable(
                name: "visit_evidence");

            migrationBuilder.DropTable(
                name: "visit_incidents");

            migrationBuilder.DropTable(
                name: "visit_materials");

            migrationBuilder.DropTable(
                name: "visit_status_history");

            migrationBuilder.DropTable(
                name: "visit_time_entries");

            migrationBuilder.DropTable(
                name: "work_order_required_skills");

            migrationBuilder.DropTable(
                name: "work_order_checklist_templates");

            migrationBuilder.DropTable(
                name: "visits");

            migrationBuilder.DropTable(
                name: "work_orders");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:message_visibility", "customer,internal")
                .Annotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .Annotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .Annotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .OldAnnotation("Npgsql:Enum:catalog_item_type", "service,product")
                .OldAnnotation("Npgsql:Enum:customer_type", "person,company")
                .OldAnnotation("Npgsql:Enum:message_visibility", "customer,internal")
                .OldAnnotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:visit_status", "unscheduled,scheduled,assigned,on_the_way,in_progress,paused,completed,needs_correction,approved,cancelled")
                .OldAnnotation("Npgsql:Enum:work_order_status", "draft,ready_to_schedule,scheduled,in_progress,completed,approved_for_billing,cancelled");
        }
    }
}
