using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "billing_follow_up_at",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "billing_follow_up_by_user_id",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_review_note",
                table: "work_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_total",
                table: "invoices",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "payment_terms",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_billing_follow_up_by_user_id",
                table: "work_orders",
                column: "billing_follow_up_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_billing_follow_up_pair",
                table: "work_orders",
                sql: "(billing_follow_up_at IS NULL) = (billing_follow_up_by_user_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_work_order_active",
                table: "invoices",
                columns: new[] { "organization_id", "work_order_id" },
                unique: true,
                filter: "status <> 'void'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_discount_total",
                table: "invoices",
                sql: "discount_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_payment_terms",
                table: "invoices",
                sql: "payment_terms IN ('due_upon_receipt','net_15','net_30')");

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_users_billing_follow_up_by_user_id",
                table: "work_orders",
                column: "billing_follow_up_by_user_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_users_billing_follow_up_by_user_id",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_billing_follow_up_by_user_id",
                table: "work_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_billing_follow_up_pair",
                table: "work_orders");

            migrationBuilder.DropIndex(
                name: "ux_invoices_work_order_active",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_discount_total",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_payment_terms",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "billing_follow_up_at",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "billing_follow_up_by_user_id",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "billing_review_note",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "discount_total",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "payment_terms",
                table: "invoices");
        }
    }
}
