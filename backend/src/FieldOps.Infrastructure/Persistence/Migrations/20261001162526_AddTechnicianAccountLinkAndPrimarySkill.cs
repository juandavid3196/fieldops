using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicianAccountLinkAndPrimarySkill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "technician_skills",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ux_technician_skills_primary",
                table: "technician_skills",
                column: "technician_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ux_technician_profiles_org_user",
                table: "technician_profiles",
                column: "organization_user_id",
                unique: true,
                filter: "organization_user_id IS NOT NULL");

            // Expression index EF cannot model, so it lives only here. It fails on existing duplicate
            // emails instead of deduplicating them.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_technician_profiles_org_email
                ON technician_profiles (organization_id, lower(email))
                WHERE email IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_technician_profiles_org_email;");

            migrationBuilder.DropIndex(
                name: "ux_technician_skills_primary",
                table: "technician_skills");

            migrationBuilder.DropIndex(
                name: "ux_technician_profiles_org_user",
                table: "technician_profiles");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "technician_skills");
        }
    }
}
