using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileJobProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_visit_materials_visit_id",
                table: "visit_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_evidence_size_bytes",
                table: "visit_evidence");

            migrationBuilder.AddColumn<Guid>(
                name: "planned_material_id",
                table: "visit_materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "visit_evidence",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<byte[]>(
                name: "content",
                table: "visit_evidence",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_materials_planned_material_id",
                table: "visit_materials",
                column: "planned_material_id");

            migrationBuilder.CreateIndex(
                name: "ux_visit_materials_planned",
                table: "visit_materials",
                columns: new[] { "visit_id", "planned_material_id" },
                unique: true,
                filter: "planned_material_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_evidence_content_or_storage",
                table: "visit_evidence",
                sql: "content IS NOT NULL OR storage_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_evidence_mime_type",
                table: "visit_evidence",
                sql: "mime_type IN ('image/jpeg', 'image/png')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_evidence_size_bytes",
                table: "visit_evidence",
                sql: "size_bytes > 0 AND size_bytes <= 10485760");

            migrationBuilder.AddForeignKey(
                name: "fk_visit_materials_work_order_planned_materials_planned_materi",
                table: "visit_materials",
                column: "planned_material_id",
                principalTable: "work_order_planned_materials",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_visit_materials_work_order_planned_materials_planned_materi",
                table: "visit_materials");

            migrationBuilder.DropIndex(
                name: "ix_visit_materials_planned_material_id",
                table: "visit_materials");

            migrationBuilder.DropIndex(
                name: "ux_visit_materials_planned",
                table: "visit_materials");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_evidence_content_or_storage",
                table: "visit_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_evidence_mime_type",
                table: "visit_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_visit_evidence_size_bytes",
                table: "visit_evidence");

            migrationBuilder.DropColumn(
                name: "planned_material_id",
                table: "visit_materials");

            migrationBuilder.DropColumn(
                name: "content",
                table: "visit_evidence");

            migrationBuilder.AlterColumn<string>(
                name: "storage_key",
                table: "visit_evidence",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_materials_visit_id",
                table: "visit_materials",
                column: "visit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_visit_evidence_size_bytes",
                table: "visit_evidence",
                sql: "size_bytes > 0");
        }
    }
}
