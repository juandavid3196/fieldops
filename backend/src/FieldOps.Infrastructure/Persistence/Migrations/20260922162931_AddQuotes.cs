using System;
using System.Net;
using FieldOps.Domain.Catalog;
using FieldOps.Domain.Quotes;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotes : Migration
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
                .OldAnnotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .OldAnnotation("Npgsql:Enum:catalog_item_type", "service,product")
                .OldAnnotation("Npgsql:Enum:customer_type", "person,company")
                .OldAnnotation("Npgsql:Enum:message_visibility", "customer,internal")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled");

            migrationBuilder.CreateTable(
                name: "quote_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    quote_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    line_type = table.Column<CatalogItemType>(type: "catalog_item_type", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, defaultValue: 0m),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false, defaultValue: 0m),
                    line_subtotal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    line_tax = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_lines", x => x.id);
                    table.CheckConstraint("ck_quote_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_quote_lines_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_quote_lines_catalog_items_catalog_item_id",
                        column: x => x.catalog_item_id,
                        principalTable: "catalog_items",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "quote_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    quote_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response = table.Column<QuoteStatus>(type: "quote_status", nullable: false),
                    responder_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    responder_contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_responses", x => x.id);
                    table.CheckConstraint("ck_quote_responses_response", "response IN ('approved','rejected','clarification_requested')");
                    table.ForeignKey(
                        name: "fk_quote_responses_customer_contacts_responder_contact_id",
                        column: x => x.responder_contact_id,
                        principalTable: "customer_contacts",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "quote_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    customer_notes = table.Column<string>(type: "text", nullable: true),
                    internal_notes = table.Column<string>(type: "text", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_immutable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_versions", x => x.id);
                    table.UniqueConstraint("ak_quote_versions_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_quote_versions_subtotal", "subtotal >= 0");
                    table.CheckConstraint("ck_quote_versions_tax_total", "tax_total >= 0");
                    table.CheckConstraint("ck_quote_versions_total", "total >= 0");
                    table.CheckConstraint("ck_quote_versions_version_no", "version_no > 0");
                    table.ForeignKey(
                        name: "fk_quote_versions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quote_versions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    property_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quote_number = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<QuoteStatus>(type: "quote_status", nullable: false),
                    current_version_no = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    approved_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotes", x => x.id);
                    table.UniqueConstraint("ak_quotes_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_quotes_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quotes_customers_organization_id_customer_id",
                        columns: x => new { x.organization_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_quotes_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quotes_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_quotes_quote_versions_approved_version_id",
                        column: x => x.approved_version_id,
                        principalTable: "quote_versions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quotes_service_requests_organization_id_request_id",
                        columns: x => new { x.organization_id, x.request_id },
                        principalTable: "service_requests",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_quotes_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_catalog_item_id",
                table: "quote_lines",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_quote_version_id",
                table: "quote_lines",
                column: "quote_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_responses_quote_version_id",
                table: "quote_responses",
                column: "quote_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_responses_responder_contact_id",
                table: "quote_responses",
                column: "responder_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_versions_created_by_user_id",
                table: "quote_versions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_versions_organization_id_quote_id",
                table: "quote_versions",
                columns: new[] { "organization_id", "quote_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_versions_quote_id_version_no",
                table: "quote_versions",
                columns: new[] { "quote_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_approved_version_id",
                table: "quotes",
                column: "approved_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_branch_id",
                table: "quotes",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_created_by_user_id",
                table: "quotes",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_organization_id_customer_id",
                table: "quotes",
                columns: new[] { "organization_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_organization_id_property_id",
                table: "quotes",
                columns: new[] { "organization_id", "property_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_organization_id_quote_number",
                table: "quotes",
                columns: new[] { "organization_id", "quote_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_organization_id_request_id",
                table: "quotes",
                columns: new[] { "organization_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_status",
                table: "quotes",
                columns: new[] { "organization_id", "status", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.AddForeignKey(
                name: "fk_quote_lines_quote_versions_quote_version_id",
                table: "quote_lines",
                column: "quote_version_id",
                principalTable: "quote_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_quote_responses_quote_versions_quote_version_id",
                table: "quote_responses",
                column: "quote_version_id",
                principalTable: "quote_versions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quote_versions_quotes_organization_id_quote_id",
                table: "quote_versions",
                columns: new[] { "organization_id", "quote_id" },
                principalTable: "quotes",
                principalColumns: new[] { "organization_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_quotes_quote_versions_approved_version_id",
                table: "quotes");

            migrationBuilder.DropTable(
                name: "quote_lines");

            migrationBuilder.DropTable(
                name: "quote_responses");

            migrationBuilder.DropTable(
                name: "quote_versions");

            migrationBuilder.DropTable(
                name: "quotes");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:message_visibility", "customer,internal")
                .Annotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .Annotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .OldAnnotation("Npgsql:Enum:catalog_item_type", "service,product")
                .OldAnnotation("Npgsql:Enum:customer_type", "person,company")
                .OldAnnotation("Npgsql:Enum:message_visibility", "customer,internal")
                .OldAnnotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled");
        }
    }
}
