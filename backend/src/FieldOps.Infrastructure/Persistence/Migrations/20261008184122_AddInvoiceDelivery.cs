using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "customer_snapshot",
                table: "invoices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_message",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recipient_email",
                table: "invoices",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_access_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_access_tokens", x => x.id);
                    table.CheckConstraint("ck_invoice_access_tokens_expires_after_created", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_invoice_access_tokens_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_invoice_access_tokens_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_invoice_access_tokens_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            // SA-09 guard: a sent (or later) invoice must carry its frozen customer content, which only exists
            // for invoices sent by this feature. A non-draft, non-void row without a snapshot aborts the migration;
            // data is never altered. Runs after the column exists and before the check constraint below.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM invoices
                        WHERE status::text NOT IN ('draft', 'void') AND customer_snapshot IS NULL) THEN
                        RAISE EXCEPTION 'Cannot add the customer snapshot rule: an invoice that is neither draft nor void has no customer snapshot.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_customer_snapshot",
                table: "invoices",
                sql: "status = 'draft' OR status = 'void' OR customer_snapshot IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_access_tokens_created_by_user_id",
                table: "invoice_access_tokens",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_access_tokens_invoice",
                table: "invoice_access_tokens",
                column: "invoice_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_access_tokens_organization_id_invoice_id",
                table: "invoice_access_tokens",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_access_tokens_token_hash",
                table: "invoice_access_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_access_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_customer_snapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "customer_snapshot",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "delivery_message",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "recipient_email",
                table: "invoices");
        }
    }
}
