using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerQuoteApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_quote_responses_customer_contacts_responder_contact_id",
                table: "quote_responses");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_responses_quote_versions_quote_version_id",
                table: "quote_responses");

            migrationBuilder.DropForeignKey(
                name: "fk_quotes_quote_versions_approved_version_id",
                table: "quotes");

            migrationBuilder.DropIndex(
                name: "ix_quotes_approved_version_id",
                table: "quotes");

            migrationBuilder.DropIndex(
                name: "ix_quote_versions_organization_id_quote_id",
                table: "quote_versions");

            migrationBuilder.DropIndex(
                name: "ix_quote_responses_quote_version_id",
                table: "quote_responses");

            migrationBuilder.DropIndex(
                name: "ix_quote_responses_responder_contact_id",
                table: "quote_responses");

            migrationBuilder.DropIndex(
                name: "ix_quote_lines_organization_id_quote_version_id",
                table: "quote_lines");

            migrationBuilder.AddColumn<decimal>(
                name: "discount_total",
                table: "quote_responses",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "quote_responses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "subtotal",
                table: "quote_responses",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_total",
                table: "quote_responses",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "quote_responses",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_quote_versions_organization_id_quote_id_id",
                table: "quote_versions",
                columns: new[] { "organization_id", "quote_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_quote_responses_organization_id_quote_version_id_id",
                table: "quote_responses",
                columns: new[] { "organization_id", "quote_version_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_quote_lines_organization_id_quote_version_id_id",
                table: "quote_lines",
                columns: new[] { "organization_id", "quote_version_id", "id" });

            migrationBuilder.CreateTable(
                name: "quote_response_optional_lines",
                columns: table => new
                {
                    quote_response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_version_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_response_optional_lines", x => new { x.quote_response_id, x.quote_line_id });
                    table.ForeignKey(
                        name: "fk_quote_response_optional_lines_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quote_response_optional_lines_quote_lines_organization_id_q",
                        columns: x => new { x.organization_id, x.quote_version_id, x.quote_line_id },
                        principalTable: "quote_lines",
                        principalColumns: new[] { "organization_id", "quote_version_id", "id" });
                    table.ForeignKey(
                        name: "fk_quote_response_optional_lines_quote_responses_organization_",
                        columns: x => new { x.organization_id, x.quote_version_id, x.quote_response_id },
                        principalTable: "quote_responses",
                        principalColumns: new[] { "organization_id", "quote_version_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_quotes_organization_id_id_approved_version_id",
                table: "quotes",
                columns: new[] { "organization_id", "id", "approved_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_responses_organization_id_responder_contact_id",
                table: "quote_responses",
                columns: new[] { "organization_id", "responder_contact_id" });

            migrationBuilder.CreateIndex(
                name: "ux_quote_responses_clarification",
                table: "quote_responses",
                column: "quote_version_id",
                unique: true,
                filter: "response = 'clarification_requested'");

            migrationBuilder.CreateIndex(
                name: "ux_quote_responses_final",
                table: "quote_responses",
                column: "quote_version_id",
                unique: true,
                filter: "response IN ('approved','rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_responses_discount_total",
                table: "quote_responses",
                sql: "discount_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_responses_subtotal",
                table: "quote_responses",
                sql: "subtotal >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_responses_tax_total",
                table: "quote_responses",
                sql: "tax_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_responses_total",
                table: "quote_responses",
                sql: "total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_responses_totals",
                table: "quote_responses",
                sql: "(response = 'approved') = (subtotal IS NOT NULL AND discount_total IS NOT NULL AND tax_total IS NOT NULL AND total IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_responses_totals_null",
                table: "quote_responses",
                sql: "response = 'approved' OR (subtotal IS NULL AND discount_total IS NULL AND tax_total IS NULL AND total IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_quote_response_optional_lines_organization_id_quote_version",
                table: "quote_response_optional_lines",
                columns: new[] { "organization_id", "quote_version_id", "quote_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_response_optional_lines_organization_id_quote_version1",
                table: "quote_response_optional_lines",
                columns: new[] { "organization_id", "quote_version_id", "quote_response_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_quote_responses_customer_contacts_organization_id_responder",
                table: "quote_responses",
                columns: new[] { "organization_id", "responder_contact_id" },
                principalTable: "customer_contacts",
                principalColumns: new[] { "organization_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_quote_responses_organizations_organization_id",
                table: "quote_responses",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quote_responses_quote_versions_organization_id_quote_versio",
                table: "quote_responses",
                columns: new[] { "organization_id", "quote_version_id" },
                principalTable: "quote_versions",
                principalColumns: new[] { "organization_id", "id" });

            migrationBuilder.AddForeignKey(
                name: "fk_quotes_quote_versions_organization_id_id_approved_version_id",
                table: "quotes",
                columns: new[] { "organization_id", "id", "approved_version_id" },
                principalTable: "quote_versions",
                principalColumns: new[] { "organization_id", "quote_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_quote_responses_customer_contacts_organization_id_responder",
                table: "quote_responses");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_responses_organizations_organization_id",
                table: "quote_responses");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_responses_quote_versions_organization_id_quote_versio",
                table: "quote_responses");

            migrationBuilder.DropForeignKey(
                name: "fk_quotes_quote_versions_organization_id_id_approved_version_id",
                table: "quotes");

            migrationBuilder.DropTable(
                name: "quote_response_optional_lines");

            migrationBuilder.DropIndex(
                name: "ix_quotes_organization_id_id_approved_version_id",
                table: "quotes");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_quote_versions_organization_id_quote_id_id",
                table: "quote_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_quote_responses_organization_id_quote_version_id_id",
                table: "quote_responses");

            migrationBuilder.DropIndex(
                name: "ix_quote_responses_organization_id_responder_contact_id",
                table: "quote_responses");

            migrationBuilder.DropIndex(
                name: "ux_quote_responses_clarification",
                table: "quote_responses");

            migrationBuilder.DropIndex(
                name: "ux_quote_responses_final",
                table: "quote_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_responses_discount_total",
                table: "quote_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_responses_subtotal",
                table: "quote_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_responses_tax_total",
                table: "quote_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_responses_total",
                table: "quote_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_responses_totals",
                table: "quote_responses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_responses_totals_null",
                table: "quote_responses");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_quote_lines_organization_id_quote_version_id_id",
                table: "quote_lines");

            migrationBuilder.DropColumn(
                name: "discount_total",
                table: "quote_responses");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "quote_responses");

            migrationBuilder.DropColumn(
                name: "subtotal",
                table: "quote_responses");

            migrationBuilder.DropColumn(
                name: "tax_total",
                table: "quote_responses");

            migrationBuilder.DropColumn(
                name: "total",
                table: "quote_responses");

            migrationBuilder.CreateIndex(
                name: "ix_quotes_approved_version_id",
                table: "quotes",
                column: "approved_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_versions_organization_id_quote_id",
                table: "quote_versions",
                columns: new[] { "organization_id", "quote_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_responses_quote_version_id",
                table: "quote_responses",
                column: "quote_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_responses_responder_contact_id",
                table: "quote_responses",
                column: "responder_contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_organization_id_quote_version_id",
                table: "quote_lines",
                columns: new[] { "organization_id", "quote_version_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_quote_responses_customer_contacts_responder_contact_id",
                table: "quote_responses",
                column: "responder_contact_id",
                principalTable: "customer_contacts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quote_responses_quote_versions_quote_version_id",
                table: "quote_responses",
                column: "quote_version_id",
                principalTable: "quote_versions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quotes_quote_versions_approved_version_id",
                table: "quotes",
                column: "approved_version_id",
                principalTable: "quote_versions",
                principalColumn: "id");
        }
    }
}
