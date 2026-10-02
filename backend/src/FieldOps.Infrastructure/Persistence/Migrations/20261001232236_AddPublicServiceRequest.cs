using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicServiceRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_request_attachments_size_bytes",
                table: "request_attachments");

            migrationBuilder.AddColumn<string>(
                name: "availability_preferences",
                table: "service_requests",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "catalog_item_id",
                table: "service_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consent_at",
                table: "service_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_active_damage",
                table: "service_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "urgency",
                table: "service_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "standard");

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "request_attachments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<byte[]>(
                name: "content",
                table: "request_attachments",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "next_request_number",
                table: "organizations",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<string>(
                name: "request_prefix",
                table: "organizations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "REQ");

            // public_slug: add nullable, backfill (BR-21), then NOT NULL.
            migrationBuilder.AddColumn<string>(
                name: "public_slug",
                table: "organizations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            // Same rules as PublicSlugGenerator: diacritics removed, lowercase,
            // runs outside a-z0-9 become one hyphen, trimmed, empty becomes
            // 'organization'; lowest free -2, -3, ... suffix with the base truncated
            // to 60 - length(suffix); organizations processed in created_at, id order.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    org record;
                    base_slug text;
                    candidate text;
                    suffix text;
                    n integer;
                BEGIN
                    FOR org IN SELECT id, name FROM organizations ORDER BY created_at, id LOOP
                        base_slug := lower(regexp_replace(normalize(org.name, NFD), '[̀-ͯ]', '', 'g'));
                        base_slug := btrim(regexp_replace(base_slug, '[^a-z0-9]+', '-', 'g'), '-');

                        IF base_slug = '' THEN
                            base_slug := 'organization';
                        END IF;

                        n := 1;

                        LOOP
                            IF n = 1 THEN
                                candidate := regexp_replace(left(base_slug, 60), '-+$', '');
                            ELSE
                                suffix := '-' || n::text;
                                candidate := regexp_replace(left(base_slug, 60 - length(suffix)), '-+$', '') || suffix;
                            END IF;

                            EXIT WHEN NOT EXISTS (SELECT 1 FROM organizations WHERE public_slug = candidate);
                            n := n + 1;
                        END LOOP;

                        UPDATE organizations SET public_slug = candidate WHERE id = org.id;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "public_slug",
                table: "organizations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60,
                oldNullable: true);

            // Continue numbering after the highest existing request number.
            migrationBuilder.Sql(
                """
                UPDATE organizations AS o
                SET next_request_number = r.max_number + 1
                FROM (
                    SELECT organization_id, max(request_number) AS max_number
                    FROM service_requests
                    GROUP BY organization_id
                ) AS r
                WHERE r.organization_id = o.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_organization_id_catalog_item_id",
                table: "service_requests",
                columns: new[] { "organization_id", "catalog_item_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_service_requests_urgency",
                table: "service_requests",
                sql: "urgency IN ('standard', 'urgent', 'emergency')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_request_attachments_content_or_storage",
                table: "request_attachments",
                sql: "content IS NOT NULL OR storage_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_request_attachments_mime_type",
                table: "request_attachments",
                sql: "mime_type IN ('image/jpeg', 'image/png', 'application/pdf')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_request_attachments_size_bytes",
                table: "request_attachments",
                sql: "size_bytes > 0 AND size_bytes <= 10485760");

            migrationBuilder.CreateIndex(
                name: "ux_organizations_public_slug",
                table: "organizations",
                column: "public_slug",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_public_slug",
                table: "organizations",
                sql: "public_slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(public_slug) BETWEEN 1 AND 60");

            migrationBuilder.AddForeignKey(
                name: "fk_service_requests_catalog_items_organization_id_catalog_item",
                table: "service_requests",
                columns: new[] { "organization_id", "catalog_item_id" },
                principalTable: "catalog_items",
                principalColumns: new[] { "organization_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_service_requests_catalog_items_organization_id_catalog_item",
                table: "service_requests");

            migrationBuilder.DropIndex(
                name: "ix_service_requests_organization_id_catalog_item_id",
                table: "service_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_service_requests_urgency",
                table: "service_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_request_attachments_content_or_storage",
                table: "request_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_request_attachments_mime_type",
                table: "request_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_request_attachments_size_bytes",
                table: "request_attachments");

            migrationBuilder.DropIndex(
                name: "ux_organizations_public_slug",
                table: "organizations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_public_slug",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "availability_preferences",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "catalog_item_id",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "consent_at",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "has_active_damage",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "urgency",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "content",
                table: "request_attachments");

            migrationBuilder.DropColumn(
                name: "next_request_number",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "public_slug",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "request_prefix",
                table: "organizations");

            // Inline public uploads have no storage key; the column becomes NOT NULL again.
            migrationBuilder.Sql("UPDATE request_attachments SET storage_key = '' WHERE storage_key IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "request_attachments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_request_attachments_size_bytes",
                table: "request_attachments",
                sql: "size_bytes > 0");
        }
    }
}
