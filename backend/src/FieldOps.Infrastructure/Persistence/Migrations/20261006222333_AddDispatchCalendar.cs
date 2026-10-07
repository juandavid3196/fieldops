using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_visit_assignments_visit_id_technician_id_unassigned_at",
                table: "visit_assignments");

            migrationBuilder.RenameIndex(
                name: "ix_visit_assignments_active_technician_unique",
                table: "visit_assignments",
                newName: "ux_visit_assignments_active");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "arrival_window_end",
                table: "visits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "arrival_window_start",
                table: "visits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "dispatch_note",
                table: "visits",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "preferred_end",
                table: "visits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "preferred_start",
                table: "visits",
                type: "timestamp with time zone",
                nullable: true);

            // BR-22 backfill: visit #1 of every existing work order takes the work order preferred window.
            migrationBuilder.Sql(
                """
                UPDATE visits v
                SET preferred_start = w.preferred_start, preferred_end = w.preferred_end
                FROM work_orders w
                WHERE w.id = v.work_order_id
                  AND w.organization_id = v.organization_id
                  AND v.visit_number = 1
                  AND v.preferred_start IS NULL
                  AND w.preferred_start IS NOT NULL;
                """);

            // Legacy assignments all defaulted to primary: keep the earliest active one so the one-primary index can be created.
            migrationBuilder.Sql(
                """
                UPDATE visit_assignments a
                SET is_primary = false
                WHERE a.unassigned_at IS NULL
                  AND a.is_primary
                  AND EXISTS (
                    SELECT 1 FROM visit_assignments b
                    WHERE b.visit_id = a.visit_id
                      AND b.unassigned_at IS NULL
                      AND b.is_primary
                      AND (b.assigned_at, b.id) < (a.assigned_at, a.id));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_visits_org_status_preferred",
                table: "visits",
                columns: new[] { "organization_id", "status", "preferred_start" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_visits_arrival_window_pair",
                table: "visits",
                sql: "(arrival_window_start IS NULL) = (arrival_window_end IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visits_arrival_window_range",
                table: "visits",
                sql: "arrival_window_start IS NULL OR (scheduled_start IS NOT NULL AND arrival_window_start <= scheduled_start AND scheduled_start <= arrival_window_end)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visits_preferred_range",
                table: "visits",
                sql: "(preferred_start IS NULL) = (preferred_end IS NULL) AND (preferred_start IS NULL OR preferred_start < preferred_end)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visits_schedule_pair",
                table: "visits",
                sql: "(scheduled_start IS NULL) = (scheduled_end IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_visit_assignments_primary",
                table: "visit_assignments",
                column: "visit_id",
                unique: true,
                filter: "is_primary AND unassigned_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_visits_org_status_preferred",
                table: "visits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visits_arrival_window_pair",
                table: "visits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visits_arrival_window_range",
                table: "visits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visits_preferred_range",
                table: "visits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visits_schedule_pair",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ux_visit_assignments_primary",
                table: "visit_assignments");

            migrationBuilder.DropColumn(
                name: "arrival_window_end",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "arrival_window_start",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "dispatch_note",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "preferred_end",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "preferred_start",
                table: "visits");

            migrationBuilder.RenameIndex(
                name: "ux_visit_assignments_active",
                table: "visit_assignments",
                newName: "ix_visit_assignments_active_technician_unique");

            migrationBuilder.CreateIndex(
                name: "ix_visit_assignments_visit_id_technician_id_unassigned_at",
                table: "visit_assignments",
                columns: new[] { "visit_id", "technician_id", "unassigned_at" },
                unique: true);
        }
    }
}
