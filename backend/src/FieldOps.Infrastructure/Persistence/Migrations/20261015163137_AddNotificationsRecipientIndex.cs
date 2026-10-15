using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsRecipientIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_organization_id",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient",
                table: "notifications",
                columns: new[] { "organization_id", "recipient_user_id", "created_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_recipient",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_organization_id",
                table: "notifications",
                column: "organization_id");
        }
    }
}
