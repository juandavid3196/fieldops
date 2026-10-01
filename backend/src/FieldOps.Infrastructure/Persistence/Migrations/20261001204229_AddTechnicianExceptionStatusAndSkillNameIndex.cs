using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicianExceptionStatusAndSkillNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_skills_organization_id_name",
                table: "skills");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "technician_exceptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "technician_exceptions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "technician_exceptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddCheckConstraint(
                name: "ck_technician_exceptions_status",
                table: "technician_exceptions",
                sql: "status IN ('active','cancelled')");

            // BR-24: case-insensitive uniqueness. EF cannot model an expression index, so it is created with SQL.
            // Case-duplicate names already stored make this statement fail; they are never deduplicated here.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_skills_org_normalized_name ON skills (organization_id, lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_skills_org_normalized_name;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_technician_exceptions_status",
                table: "technician_exceptions");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "technician_exceptions");

            migrationBuilder.DropColumn(
                name: "status",
                table: "technician_exceptions");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "technician_exceptions");

            migrationBuilder.CreateIndex(
                name: "ix_skills_organization_id_name",
                table: "skills",
                columns: new[] { "organization_id", "name" },
                unique: true);
        }
    }
}
