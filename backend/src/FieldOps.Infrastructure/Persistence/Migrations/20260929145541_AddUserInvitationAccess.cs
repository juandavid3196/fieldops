using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserInvitationAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_invitations_organization_id",
                table: "user_invitations");

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "user_invitations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_all_branches",
                table: "user_invitations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "user_invitations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "link_team_profile",
                table: "user_invitations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The empty default only backfills existing rows; the schema has
            // no default on the name columns.
            migrationBuilder.Sql("ALTER TABLE user_invitations ALTER COLUMN first_name DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE user_invitations ALTER COLUMN last_name DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ux_user_invitations_open_email",
                table: "user_invitations",
                columns: new[] { "organization_id", "email" },
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_invitations_expires_after_created",
                table: "user_invitations",
                sql: "expires_at > created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_user_invitations_open_email",
                table: "user_invitations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_invitations_expires_after_created",
                table: "user_invitations");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "user_invitations");

            migrationBuilder.DropColumn(
                name: "is_all_branches",
                table: "user_invitations");

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "user_invitations");

            migrationBuilder.DropColumn(
                name: "link_team_profile",
                table: "user_invitations");

            migrationBuilder.CreateIndex(
                name: "ix_user_invitations_organization_id",
                table: "user_invitations",
                column: "organization_id");
        }
    }
}
