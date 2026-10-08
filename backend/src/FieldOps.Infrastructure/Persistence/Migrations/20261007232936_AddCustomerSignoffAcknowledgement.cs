using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSignoffAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SA-03: acknowledgement_method and recorded_by_user_id are NOT NULL without a default on purpose. If a
            // customer_signoffs row exists (no feature wrote one before), the migration fails instead of inventing values.
            migrationBuilder.AddColumn<string>(
                name: "acknowledgement_method",
                table: "customer_signoffs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false);

            migrationBuilder.AddColumn<Guid>(
                name: "recorded_by_user_id",
                table: "customer_signoffs",
                type: "uuid",
                nullable: false);

            migrationBuilder.AddColumn<bool>(
                name: "review_confirmed",
                table: "customer_signoffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "signature_content",
                table: "customer_signoffs",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signature_mime_type",
                table: "customer_signoffs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signer_relationship",
                table: "customer_signoffs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_signoffs_recorded_by_user_id",
                table: "customer_signoffs",
                column: "recorded_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_signoffs_acknowledgement_method",
                table: "customer_signoffs",
                sql: "acknowledgement_method IN ('signed','customer_absent','customer_refused','remote_confirmation')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_signoffs_signature_content_pair",
                table: "customer_signoffs",
                sql: "(signature_content IS NULL) = (signature_mime_type IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_signoffs_signature_mime_type",
                table: "customer_signoffs",
                sql: "signature_mime_type = 'image/png'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_signoffs_signature_size",
                table: "customer_signoffs",
                sql: "signature_content IS NULL OR octet_length(signature_content) <= 524288");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_signoffs_signed_has_signature",
                table: "customer_signoffs",
                sql: "(acknowledgement_method = 'signed') = (signature_content IS NOT NULL OR signature_storage_key IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_signoffs_signer_relationship",
                table: "customer_signoffs",
                sql: "signer_relationship IN ('customer','family_member','tenant','property_manager','employee','other')");

            migrationBuilder.AddForeignKey(
                name: "fk_customer_signoffs_users_recorded_by_user_id",
                table: "customer_signoffs",
                column: "recorded_by_user_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_customer_signoffs_users_recorded_by_user_id",
                table: "customer_signoffs");

            migrationBuilder.DropIndex(
                name: "ix_customer_signoffs_recorded_by_user_id",
                table: "customer_signoffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_signoffs_acknowledgement_method",
                table: "customer_signoffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_signoffs_signature_content_pair",
                table: "customer_signoffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_signoffs_signature_mime_type",
                table: "customer_signoffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_signoffs_signature_size",
                table: "customer_signoffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_signoffs_signed_has_signature",
                table: "customer_signoffs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_signoffs_signer_relationship",
                table: "customer_signoffs");

            migrationBuilder.DropColumn(
                name: "acknowledgement_method",
                table: "customer_signoffs");

            migrationBuilder.DropColumn(
                name: "recorded_by_user_id",
                table: "customer_signoffs");

            migrationBuilder.DropColumn(
                name: "review_confirmed",
                table: "customer_signoffs");

            migrationBuilder.DropColumn(
                name: "signature_content",
                table: "customer_signoffs");

            migrationBuilder.DropColumn(
                name: "signature_mime_type",
                table: "customer_signoffs");

            migrationBuilder.DropColumn(
                name: "signer_relationship",
                table: "customer_signoffs");
        }
    }
}
