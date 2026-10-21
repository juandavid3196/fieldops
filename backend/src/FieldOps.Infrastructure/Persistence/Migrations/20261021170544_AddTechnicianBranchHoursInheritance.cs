using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicianBranchHoursInheritance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "uses_branch_hours",
                table: "technician_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // team-availability-skills BR-22: profiles that already have personal weekly rows keep them.
            migrationBuilder.Sql(
                """
                UPDATE technician_profiles SET uses_branch_hours = false
                WHERE EXISTS (SELECT 1 FROM technician_weekly_availability w WHERE w.technician_id = technician_profiles.id);
                """);

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "technician_exceptions",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "uses_branch_hours",
                table: "technician_profiles");

            migrationBuilder.AlterColumn<string>(
                name: "reason",
                table: "technician_exceptions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldNullable: true);
        }
    }
}
