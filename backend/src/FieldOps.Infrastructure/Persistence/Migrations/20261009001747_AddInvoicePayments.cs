using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SA-12: received_by_user_id and idempotency_key are NOT NULL without a default, so existing payments cannot
            // be backfilled. Fail explicitly instead of inventing values (no feature wrote payments before this one).
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM payments) THEN
                        RAISE EXCEPTION 'AddInvoicePayments: payments has rows; received_by_user_id and idempotency_key cannot be backfilled.';
                    END IF;
                END $$;
                """);

            migrationBuilder.RenameIndex(
                name: "ix_payment_allocations_invoice_id",
                table: "payment_allocations",
                newName: "ix_payment_allocations_invoice");

            migrationBuilder.AddColumn<Guid>(
                name: "idempotency_key",
                table: "payments",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "received_by_user_id",
                table: "payments",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<long>(
                name: "next_payment_number",
                table: "organizations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "payment_prefix",
                table: "organizations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "PAY");

            migrationBuilder.CreateIndex(
                name: "ix_payments_org_paid_at",
                table: "payments",
                columns: new[] { "organization_id", "paid_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_id_idempotency_key",
                table: "payments",
                columns: new[] { "organization_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_received_by_user_id",
                table: "payments",
                column: "received_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_org_issue_date",
                table: "invoices",
                columns: new[] { "organization_id", "issue_date" });

            migrationBuilder.AddForeignKey(
                name: "fk_payments_users_received_by_user_id",
                table: "payments",
                column: "received_by_user_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payments_users_received_by_user_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_org_paid_at",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_organization_id_idempotency_key",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_received_by_user_id",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_invoices_org_issue_date",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "received_by_user_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "next_payment_number",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "payment_prefix",
                table: "organizations");

            migrationBuilder.RenameIndex(
                name: "ix_payment_allocations_invoice",
                table: "payment_allocations",
                newName: "ix_payment_allocations_invoice_id");
        }
    }
}
