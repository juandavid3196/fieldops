using System;
using FieldOps.Domain.Invoices;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerInvoicePayments : Migration
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
                .Annotation("Npgsql:Enum:notification_status", "pending,sent,failed,read")
                .Annotation("Npgsql:Enum:payment_attempt_status", "pending,succeeded,failed,partially_refunded,refunded")
                .Annotation("Npgsql:Enum:payment_method", "cash,bank_transfer,card_external,check,other,card_online")
                .Annotation("Npgsql:Enum:payment_status", "succeeded,partially_refunded,refunded")
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
                .OldAnnotation("Npgsql:Enum:notification_status", "pending,sent,failed,read")
                .OldAnnotation("Npgsql:Enum:payment_method", "cash,bank_transfer,card_external,check,other")
                .OldAnnotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:visit_status", "unscheduled,scheduled,assigned,on_the_way,in_progress,paused,completed,needs_correction,approved,cancelled")
                .OldAnnotation("Npgsql:Enum:work_order_status", "draft,ready_to_schedule,scheduled,in_progress,completed,approved_for_billing,cancelled");

            migrationBuilder.AlterColumn<Guid>(
                name: "received_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "card_brand",
                table: "payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "card_last4",
                table: "payments",
                type: "character(4)",
                fixedLength: true,
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "receipt_number",
                table: "payments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "receipt_sent_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refunded_amount",
                table: "payments",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<PaymentStatus>(
                name: "status",
                table: "payments",
                type: "payment_status",
                nullable: false,
                defaultValue: PaymentStatus.Succeeded);

            migrationBuilder.AddColumn<string>(
                name: "bank_account_last4",
                table: "organizations",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "bank_account_number_ciphertext",
                table: "organizations",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bank_details_updated_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "organizations",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_routing_number",
                table: "organizations",
                type: "character(9)",
                fixedLength: true,
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_account_id",
                table: "organizations",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<PaymentMethod>(type: "payment_method", nullable: false),
                    status = table.Column<PaymentAttemptStatus>(type: "payment_attempt_status", nullable: false, defaultValue: PaymentAttemptStatus.Pending),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_payment_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    refunded_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false, defaultValue: 0m),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_invoice_payment_attempts_amount", "amount > 0");
                    table.CheckConstraint("ck_invoice_payment_attempts_method", "method::text IN ('card_online','bank_transfer')");
                    table.CheckConstraint("ck_invoice_payment_attempts_payment_link", "(status = 'succeeded' OR status = 'partially_refunded' OR status = 'refunded') = (payment_id IS NOT NULL)");
                    table.CheckConstraint("ck_invoice_payment_attempts_refunded_amount", "refunded_amount >= 0 AND refunded_amount <= amount");
                    table.ForeignKey(
                        name: "fk_invoice_payment_attempts_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_invoice_payment_attempts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoice_payment_attempts_payments_organization_id_payment_id",
                        columns: x => new { x.organization_id, x.payment_id },
                        principalTable: "payments",
                        principalColumns: new[] { "organization_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "invoice_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_reviews", x => x.id);
                    table.CheckConstraint("ck_invoice_reviews_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_invoice_reviews_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_invoice_reviews_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoice_reviews_technician_profiles_organization_id_technic",
                        columns: x => new { x.organization_id, x.technician_id },
                        principalTable: "technician_profiles",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_invoice_reviews_work_orders_organization_id_work_order_id",
                        columns: x => new { x.organization_id, x.work_order_id },
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "stripe"),
                    provider_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_webhook_events", x => x.id);
                    table.CheckConstraint("ck_payment_webhook_events_outcome", "outcome IN ('applied','no_effect','ignored','needs_attention')");
                    table.ForeignKey(
                        name: "fk_payment_webhook_events_invoice_payment_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "invoice_payment_attempts",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payment_webhook_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_id_receipt_number",
                table: "payments",
                columns: new[] { "organization_id", "receipt_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_card_online_last4",
                table: "payments",
                sql: "(method::text = 'card_online') = (card_last4 IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_card_online_no_receiver",
                table: "payments",
                sql: "(method::text = 'card_online') = (received_by_user_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_refunded_amount_range",
                table: "payments",
                sql: "refunded_amount BETWEEN 0 AND amount");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_status_refunded_full",
                table: "payments",
                sql: "(status = 'refunded') = (refunded_amount = amount)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_status_succeeded_no_refund",
                table: "payments",
                sql: "(status = 'succeeded') = (refunded_amount = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_bank_details_all_or_none",
                table: "organizations",
                sql: "(bank_name IS NULL) = (bank_account_number_ciphertext IS NULL) AND (bank_name IS NULL) = (bank_account_last4 IS NULL) AND (bank_name IS NULL) = (bank_routing_number IS NULL) AND (bank_name IS NULL) = (bank_details_updated_at IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payment_attempts_invoice",
                table: "invoice_payment_attempts",
                columns: new[] { "invoice_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payment_attempts_organization_id_idempotency_key",
                table: "invoice_payment_attempts",
                columns: new[] { "organization_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payment_attempts_organization_id_invoice_id",
                table: "invoice_payment_attempts",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payment_attempts_organization_id_payment_id",
                table: "invoice_payment_attempts",
                columns: new[] { "organization_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_payment_attempts_provider_payment_intent_id",
                table: "invoice_payment_attempts",
                column: "provider_payment_intent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoice_payment_attempts_pending",
                table: "invoice_payment_attempts",
                columns: new[] { "invoice_id", "method" },
                unique: true,
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_reviews_organization_id_invoice_id",
                table: "invoice_reviews",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_reviews_organization_id_technician_id",
                table: "invoice_reviews",
                columns: new[] { "organization_id", "technician_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_reviews_organization_id_work_order_id",
                table: "invoice_reviews",
                columns: new[] { "organization_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_reviews_work_order_id",
                table: "invoice_reviews",
                column: "work_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_attempt_id",
                table: "payment_webhook_events",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_organization_id",
                table: "payment_webhook_events",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_provider_provider_event_id",
                table: "payment_webhook_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_reviews");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "invoice_payment_attempts");

            migrationBuilder.DropIndex(
                name: "ix_payments_organization_id_receipt_number",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_card_online_last4",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_card_online_no_receiver",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_refunded_amount_range",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_status_refunded_full",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_status_succeeded_no_refund",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_bank_details_all_or_none",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "card_brand",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "card_last4",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "receipt_number",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "receipt_sent_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunded_amount",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "bank_account_last4",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "bank_account_number_ciphertext",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "bank_details_updated_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "bank_routing_number",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "stripe_account_id",
                table: "organizations");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:assessment_status", "scheduled,completed,cancelled,no_show")
                .Annotation("Npgsql:Enum:catalog_item_type", "service,product")
                .Annotation("Npgsql:Enum:customer_type", "person,company")
                .Annotation("Npgsql:Enum:invoice_status", "draft,sent,partially_paid,paid,overdue,void")
                .Annotation("Npgsql:Enum:message_visibility", "customer,internal")
                .Annotation("Npgsql:Enum:notification_status", "pending,sent,failed,read")
                .Annotation("Npgsql:Enum:payment_method", "cash,bank_transfer,card_external,check,other")
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
                .OldAnnotation("Npgsql:Enum:notification_status", "pending,sent,failed,read")
                .OldAnnotation("Npgsql:Enum:payment_attempt_status", "pending,succeeded,failed,partially_refunded,refunded")
                .OldAnnotation("Npgsql:Enum:payment_method", "cash,bank_transfer,card_external,check,other,card_online")
                .OldAnnotation("Npgsql:Enum:payment_status", "succeeded,partially_refunded,refunded")
                .OldAnnotation("Npgsql:Enum:quote_status", "draft,sent,approved,rejected,clarification_requested,expired,cancelled")
                .OldAnnotation("Npgsql:Enum:request_status", "new,needs_review,assessment_scheduled,ready_for_quote,quoted,converted,cancelled")
                .OldAnnotation("Npgsql:Enum:user_status", "pending,active,suspended,disabled")
                .OldAnnotation("Npgsql:Enum:visit_status", "unscheduled,scheduled,assigned,on_the_way,in_progress,paused,completed,needs_correction,approved,cancelled")
                .OldAnnotation("Npgsql:Enum:work_order_status", "draft,ready_to_schedule,scheduled,in_progress,completed,approved_for_billing,cancelled");

            migrationBuilder.AlterColumn<Guid>(
                name: "received_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
