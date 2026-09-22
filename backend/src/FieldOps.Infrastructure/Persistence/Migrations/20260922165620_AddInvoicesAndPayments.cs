using System;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicesAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:invoice_status", "draft,sent,partially_paid,paid,overdue,void")
                .Annotation("Npgsql:Enum:message_visibility", "customer,internal")
                .Annotation("Npgsql:Enum:payment_method", "cash,bank_transfer,card_external,check,other")
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
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:visit_status", "unscheduled,scheduled,assigned,on_the_way,in_progress,paused,completed,needs_correction,approved,cancelled")
                .OldAnnotation("Npgsql:Enum:work_order_status", "draft,ready_to_schedule,scheduled,in_progress,completed,approved_for_billing,cancelled");

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<long>(type: "bigint", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<InvoiceStatus>(type: "invoice_status", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    tax_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, defaultValue: 0m),
                    balance_due = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "text", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.UniqueConstraint("ak_invoices_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_invoices_amount_paid", "amount_paid >= 0");
                    table.CheckConstraint("ck_invoices_amount_paid_le_total", "amount_paid <= total");
                    table.CheckConstraint("ck_invoices_balance_due", "balance_due >= 0");
                    table.CheckConstraint("ck_invoices_date_range", "due_date IS NULL OR issue_date IS NULL OR due_date >= issue_date");
                    table.CheckConstraint("ck_invoices_subtotal", "subtotal >= 0");
                    table.CheckConstraint("ck_invoices_tax_total", "tax_total >= 0");
                    table.CheckConstraint("ck_invoices_total", "total >= 0");
                    table.ForeignKey(
                        name: "fk_invoices_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoices_customers_organization_id_customer_id",
                        columns: x => new { x.organization_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_invoices_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoices_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoices_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_number = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<PaymentMethod>(type: "payment_method", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_storage_key = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payments_customers_organization_id_customer_id",
                        columns: x => new { x.organization_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_payments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_quote_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_visit_material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false, defaultValue: 0m),
                    line_subtotal = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    line_tax = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_invoice_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_invoice_lines_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoice_lines_quote_lines_source_quote_line_id",
                        column: x => x.source_quote_line_id,
                        principalTable: "quote_lines",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoice_lines_visit_materials_source_visit_material_id",
                        column: x => x.source_visit_material_id,
                        principalTable: "visit_materials",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_allocations_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_invoice_id",
                table: "invoice_lines",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_source_quote_line_id",
                table: "invoice_lines",
                column: "source_quote_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_source_visit_material_id",
                table: "invoice_lines",
                column: "source_visit_material_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_branch_id",
                table: "invoices",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_created_by_user_id",
                table: "invoices",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_id_customer_id",
                table: "invoices",
                columns: new[] { "organization_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_id_invoice_number",
                table: "invoices",
                columns: new[] { "organization_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status_due",
                table: "invoices",
                columns: new[] { "organization_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_work_order_id",
                table: "invoices",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_invoice_id",
                table: "payment_allocations",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id_invoice_id",
                table: "payment_allocations",
                columns: new[] { "payment_id", "invoice_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_customer_date",
                table: "payments",
                columns: new[] { "organization_id", "customer_id", "paid_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_id_payment_number",
                table: "payments",
                columns: new[] { "organization_id", "payment_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_recorded_by_user_id",
                table: "payments",
                column: "recorded_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "payments");

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
                .OldAnnotation("Npgsql:Enum:invoice_status", "draft,sent,partially_paid,paid,overdue,void")
                .OldAnnotation("Npgsql:Enum:message_visibility", "customer,internal")
                .OldAnnotation("Npgsql:Enum:payment_method", "cash,bank_transfer,card_external,check,other")
                .OldAnnotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:visit_status", "unscheduled,scheduled,assigned,on_the_way,in_progress,paused,completed,needs_correction,approved,cancelled")
                .OldAnnotation("Npgsql:Enum:work_order_status", "draft,ready_to_schedule,scheduled,in_progress,completed,approved_for_billing,cancelled");
        }
    }
}
