using System;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestsAndAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:message_visibility", "customer,internal")
                .Annotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .Annotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:catalog_item_type", "service,product")
                .OldAnnotation("Npgsql:Enum:customer_type", "person,company")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled");

            migrationBuilder.CreateTable(
                name: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_number = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    guest_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    guest_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    service_address = table.Column<string>(type: "jsonb", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    preferred_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preferred_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<RequestStatus>(type: "request_status", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "public_form"),
                    assigned_dispatcher_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_requests", x => x.id);
                    table.UniqueConstraint("ak_service_requests_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_service_requests_preferred_range", "preferred_end IS NULL OR preferred_start IS NULL OR preferred_start < preferred_end");
                    table.ForeignKey(
                        name: "fk_service_requests_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_service_requests_customer_contacts_organization_id_contact_",
                        columns: x => new { x.organization_id, x.contact_id },
                        principalTable: "customer_contacts",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_service_requests_customers_organization_id_customer_id",
                        columns: x => new { x.organization_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_service_requests_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_service_requests_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_service_requests_service_categories_organization_id_categor",
                        columns: x => new { x.organization_id, x.category_id },
                        principalTable: "service_categories",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_service_requests_users_assigned_dispatcher_user_id",
                        column: x => x.assigned_dispatcher_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<AssessmentStatus>(type: "assessment_status", nullable: false),
                    diagnosis = table.Column<string>(type: "text", nullable: true),
                    recommended_scope = table.Column<string>(type: "text", nullable: true),
                    internal_notes = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessments", x => x.id);
                    table.UniqueConstraint("ak_assessments_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_assessments_schedule_range", "scheduled_start < scheduled_end");
                    table.ForeignKey(
                        name: "fk_assessments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_assessments_service_requests_organization_id_request_id",
                        columns: x => new { x.organization_id, x.request_id },
                        principalTable: "service_requests",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_assessments_technician_profiles_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technician_profiles",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_assessments_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "request_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_attachments", x => x.id);
                    table.CheckConstraint("ck_request_attachments_size_bytes", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_request_attachments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_request_attachments_service_requests_organization_id_reques",
                        columns: x => new { x.organization_id, x.request_id },
                        principalTable: "service_requests",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_request_attachments_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "request_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visibility = table.Column<MessageVisibility>(type: "message_visibility", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_request_messages_customer_contacts_author_contact_id",
                        column: x => x.author_contact_id,
                        principalTable: "customer_contacts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_request_messages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_request_messages_service_requests_organization_id_request_id",
                        columns: x => new { x.organization_id, x.request_id },
                        principalTable: "service_requests",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_request_messages_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "request_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<RequestStatus>(type: "request_status", nullable: true),
                    to_status = table.Column<RequestStatus>(type: "request_status", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_request_status_history_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_request_status_history_service_requests_organization_id_req",
                        columns: x => new { x.organization_id, x.request_id },
                        principalTable: "service_requests",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_request_status_history_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "assessment_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assessment_attachments_assessments_assessment_id",
                        column: x => x.assessment_id,
                        principalTable: "assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_attachments_assessment_id",
                table: "assessment_attachments",
                column: "assessment_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessments_created_by_user_id",
                table: "assessments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessments_organization_id_request_id",
                table: "assessments",
                columns: new[] { "organization_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessments_schedule",
                table: "assessments",
                columns: new[] { "organization_id", "scheduled_start", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_assessments_technician_id",
                table: "assessments",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_attachments_organization_id_request_id",
                table: "request_attachments",
                columns: new[] { "organization_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_request_attachments_uploaded_by_user_id",
                table: "request_attachments",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_messages_author_contact_id",
                table: "request_messages",
                column: "author_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_messages_author_user_id",
                table: "request_messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_messages_organization_id_request_id",
                table: "request_messages",
                columns: new[] { "organization_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_request_status_history_changed_by_user_id",
                table: "request_status_history",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_status_history_organization_id_request_id",
                table: "request_status_history",
                columns: new[] { "organization_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_customer",
                table: "service_requests",
                columns: new[] { "organization_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_requests_pipeline",
                table: "service_requests",
                columns: new[] { "organization_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_assigned_dispatcher_user_id",
                table: "service_requests",
                column: "assigned_dispatcher_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_branch_id",
                table: "service_requests",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_organization_id_category_id",
                table: "service_requests",
                columns: new[] { "organization_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_organization_id_contact_id",
                table: "service_requests",
                columns: new[] { "organization_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_organization_id_property_id",
                table: "service_requests",
                columns: new[] { "organization_id", "property_id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_organization_id_request_number",
                table: "service_requests",
                columns: new[] { "organization_id", "request_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment_attachments");

            migrationBuilder.DropTable(
                name: "request_attachments");

            migrationBuilder.DropTable(
                name: "request_messages");

            migrationBuilder.DropTable(
                name: "request_status_history");

            migrationBuilder.DropTable(
                name: "assessments");

            migrationBuilder.DropTable(
                name: "service_requests");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .OldAnnotation("Npgsql:Enum:catalog_item_type", "service,product")
                .OldAnnotation("Npgsql:Enum:customer_type", "person,company")
                .OldAnnotation("Npgsql:Enum:message_visibility", "customer,internal")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled");
        }
    }
}
