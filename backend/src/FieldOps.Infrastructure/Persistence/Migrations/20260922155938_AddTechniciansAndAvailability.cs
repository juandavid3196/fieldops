using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechniciansAndAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "skills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skills", x => x.id);
                    table.UniqueConstraint("ak_skills_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_skills_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "technician_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValueSql: "'active'"),
                    color_hex = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_profiles", x => x.id);
                    table.UniqueConstraint("ak_technician_profiles_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_technician_profiles_status", "status IN ('active','inactive','suspended')");
                    table.ForeignKey(
                        name: "fk_technician_profiles_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_technician_profiles_organization_users_organization_id_orga",
                        columns: x => new { x.organization_id, x.organization_user_id },
                        principalTable: "organization_users",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_technician_profiles_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "technician_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_exceptions", x => x.id);
                    table.CheckConstraint("ck_technician_exceptions_start_end", "starts_at < ends_at");
                    table.ForeignKey(
                        name: "fk_technician_exceptions_technician_profiles_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technician_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technician_skills",
                columns: table => new
                {
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    proficiency = table.Column<short>(type: "smallint", nullable: true),
                    years_experience = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_skills", x => new { x.technician_id, x.skill_id });
                    table.CheckConstraint("ck_technician_skills_proficiency", "proficiency BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_technician_skills_skills_skill_id",
                        column: x => x.skill_id,
                        principalTable: "skills",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_technician_skills_technician_profiles_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technician_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technician_weekly_availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    capacity_percent = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)100)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_weekly_availability", x => x.id);
                    table.CheckConstraint("ck_technician_weekly_availability_capacity_percent", "capacity_percent BETWEEN 1 AND 100");
                    table.CheckConstraint("ck_technician_weekly_availability_day_of_week", "day_of_week BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_technician_weekly_availability_start_end", "start_time < end_time");
                    table.ForeignKey(
                        name: "fk_technician_weekly_availability_technician_profiles_technici",
                        column: x => x.technician_id,
                        principalTable: "technician_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technician_breaks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    availability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_breaks", x => x.id);
                    table.CheckConstraint("ck_technician_breaks_start_end", "start_time < end_time");
                    table.ForeignKey(
                        name: "fk_technician_breaks_technician_weekly_availabilities_availabi",
                        column: x => x.availability_id,
                        principalTable: "technician_weekly_availability",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_skills_organization_id_name",
                table: "skills",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_technician_breaks_availability_id",
                table: "technician_breaks",
                column: "availability_id");

            migrationBuilder.CreateIndex(
                name: "ix_technician_exceptions_technician_id",
                table: "technician_exceptions",
                column: "technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_technician_profiles_branch_id",
                table: "technician_profiles",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_technician_profiles_organization_id_employee_code",
                table: "technician_profiles",
                columns: new[] { "organization_id", "employee_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_technician_profiles_organization_id_organization_user_id",
                table: "technician_profiles",
                columns: new[] { "organization_id", "organization_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_technicians_branch_status",
                table: "technician_profiles",
                columns: new[] { "organization_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_technician_skills_skill_id",
                table: "technician_skills",
                column: "skill_id");

            migrationBuilder.CreateIndex(
                name: "ix_technician_weekly_availability_technician_id",
                table: "technician_weekly_availability",
                column: "technician_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "technician_breaks");

            migrationBuilder.DropTable(
                name: "technician_exceptions");

            migrationBuilder.DropTable(
                name: "technician_skills");

            migrationBuilder.DropTable(
                name: "technician_weekly_availability");

            migrationBuilder.DropTable(
                name: "skills");

            migrationBuilder.DropTable(
                name: "technician_profiles");
        }
    }
}
