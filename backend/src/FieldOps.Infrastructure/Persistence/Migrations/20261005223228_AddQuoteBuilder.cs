using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_quotes_organization_id_request_id",
                table: "quotes");

            migrationBuilder.AddColumn<decimal>(
                name: "discount_total",
                table: "quote_versions",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "terms",
                table: "quote_versions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_optional",
                table: "quote_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "quote_lines",
                type: "character varying(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            // Existing rows take their organization from the version (and a name from the description) before NOT NULL.
            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "quote_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE quote_lines AS line
                SET organization_id = version.organization_id,
                    name = LEFT(line.description, 160)
                FROM quote_versions AS version
                WHERE version.id = line.quote_version_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "organization_id",
                table: "quote_lines",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "assessment_attachments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<byte[]>(
                name: "content",
                table: "assessment_attachments",
                type: "bytea",
                nullable: true);

            // Existing rows take the organization of their assessment before NOT NULL (quote-builder BR-32).
            migrationBuilder.AddColumn<Guid>(
                name: "organization_id",
                table: "assessment_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE assessment_attachments AS attachment
                SET organization_id = assessment.organization_id
                FROM assessments AS assessment
                WHERE assessment.id = attachment.assessment_id;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "organization_id",
                table: "assessment_attachments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "quote_access_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quote_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quote_access_tokens", x => x.id);
                    table.CheckConstraint("ck_quote_access_tokens_expires_after_created", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_quote_access_tokens_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_quote_access_tokens_quote_versions_organization_id_quote_ve",
                        columns: x => new { x.organization_id, x.quote_version_id },
                        principalTable: "quote_versions",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_quote_access_tokens_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ux_quotes_request_open",
                table: "quotes",
                columns: new[] { "organization_id", "request_id" },
                unique: true,
                filter: "status <> 'cancelled'");

            migrationBuilder.CreateIndex(
                name: "ux_quote_versions_one_mutable",
                table: "quote_versions",
                column: "quote_id",
                unique: true,
                filter: "NOT is_immutable");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quote_versions_discount_total",
                table: "quote_versions",
                sql: "discount_total >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_quote_lines_organization_id_quote_version_id",
                table: "quote_lines",
                columns: new[] { "organization_id", "quote_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_attachments_organization_id_assessment_id",
                table: "assessment_attachments",
                columns: new[] { "organization_id", "assessment_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_assessment_attachments_content_or_storage",
                table: "assessment_attachments",
                sql: "content IS NOT NULL OR storage_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assessment_attachments_mime_type",
                table: "assessment_attachments",
                sql: "mime_type IN ('image/jpeg', 'image/png')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assessment_attachments_size_bytes",
                table: "assessment_attachments",
                sql: "size_bytes > 0 AND size_bytes <= 10485760");

            migrationBuilder.CreateIndex(
                name: "ix_quote_access_tokens_created_by_user_id",
                table: "quote_access_tokens",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_quote_access_tokens_organization_id_quote_version_id",
                table: "quote_access_tokens",
                columns: new[] { "organization_id", "quote_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_quote_access_tokens_token_hash",
                table: "quote_access_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quote_access_tokens_version",
                table: "quote_access_tokens",
                column: "quote_version_id",
                filter: "revoked_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_assessment_attachments_assessments_organization_id_assessme",
                table: "assessment_attachments",
                columns: new[] { "organization_id", "assessment_id" },
                principalTable: "assessments",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_assessment_attachments_organizations_organization_id",
                table: "assessment_attachments",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quote_lines_organizations_organization_id",
                table: "quote_lines",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_quote_lines_quote_versions_organization_id_quote_version_id",
                table: "quote_lines",
                columns: new[] { "organization_id", "quote_version_id" },
                principalTable: "quote_versions",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assessment_attachments_assessments_organization_id_assessme",
                table: "assessment_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_assessment_attachments_organizations_organization_id",
                table: "assessment_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_lines_organizations_organization_id",
                table: "quote_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_quote_lines_quote_versions_organization_id_quote_version_id",
                table: "quote_lines");

            migrationBuilder.DropTable(
                name: "quote_access_tokens");

            migrationBuilder.DropIndex(
                name: "ux_quotes_request_open",
                table: "quotes");

            migrationBuilder.DropIndex(
                name: "ux_quote_versions_one_mutable",
                table: "quote_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quote_versions_discount_total",
                table: "quote_versions");

            migrationBuilder.DropIndex(
                name: "ix_quote_lines_organization_id_quote_version_id",
                table: "quote_lines");

            migrationBuilder.DropIndex(
                name: "ix_assessment_attachments_organization_id_assessment_id",
                table: "assessment_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assessment_attachments_content_or_storage",
                table: "assessment_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assessment_attachments_mime_type",
                table: "assessment_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assessment_attachments_size_bytes",
                table: "assessment_attachments");

            migrationBuilder.DropColumn(
                name: "discount_total",
                table: "quote_versions");

            migrationBuilder.DropColumn(
                name: "terms",
                table: "quote_versions");

            migrationBuilder.DropColumn(
                name: "is_optional",
                table: "quote_lines");

            migrationBuilder.DropColumn(
                name: "name",
                table: "quote_lines");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "quote_lines");

            migrationBuilder.DropColumn(
                name: "content",
                table: "assessment_attachments");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "assessment_attachments");

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "assessment_attachments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_quotes_organization_id_request_id",
                table: "quotes",
                columns: new[] { "organization_id", "request_id" });
        }
    }
}
