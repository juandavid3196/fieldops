using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "portal_linked_at",
                table: "customer_contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "portal_updates_seen_at",
                table: "customer_contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_portal_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_portal_invitations", x => x.id);
                    table.CheckConstraint("ck_customer_portal_invitations_expires_after_created", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_customer_portal_invitations_customer_contacts_organization_",
                        columns: x => new { x.organization_id, x.contact_id },
                        principalTable: "customer_contacts",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_customer_portal_invitations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_customer_portal_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "visit_reschedule_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    preferred_date = table.Column<DateOnly>(type: "date", nullable: false),
                    time_window = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_reschedule_requests", x => x.id);
                    table.CheckConstraint("ck_visit_reschedule_requests_time_window", "time_window IN ('morning','afternoon','evening','any')");
                    table.ForeignKey(
                        name: "fk_visit_reschedule_requests_customer_contacts_organization_id",
                        columns: x => new { x.organization_id, x.contact_id },
                        principalTable: "customer_contacts",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_visit_reschedule_requests_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visit_reschedule_requests_visits_organization_id_visit_id",
                        columns: x => new { x.organization_id, x.visit_id },
                        principalTable: "visits",
                        principalColumns: new[] { "organization_id", "id" });
                });

            migrationBuilder.CreateIndex(
                name: "ux_customer_contacts_org_portal_user",
                table: "customer_contacts",
                columns: new[] { "organization_id", "portal_user_id" },
                unique: true,
                filter: "portal_user_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_contacts_portal_link",
                table: "customer_contacts",
                sql: "(portal_user_id IS NULL) = (portal_linked_at IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_customer_portal_invitations_invited_by_user_id",
                table: "customer_portal_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_portal_invitations_organization_id_contact_id",
                table: "customer_portal_invitations",
                columns: new[] { "organization_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_portal_invitations_token_hash",
                table: "customer_portal_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_customer_portal_invitations_open",
                table: "customer_portal_invitations",
                column: "contact_id",
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_visit_reschedule_requests_organization_id_contact_id",
                table: "visit_reschedule_requests",
                columns: new[] { "organization_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visit_reschedule_requests_organization_id_visit_id",
                table: "visit_reschedule_requests",
                columns: new[] { "organization_id", "visit_id" });

            migrationBuilder.CreateIndex(
                name: "ux_visit_reschedule_requests_pending",
                table: "visit_reschedule_requests",
                columns: new[] { "visit_id", "original_scheduled_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_portal_invitations");

            migrationBuilder.DropTable(
                name: "visit_reschedule_requests");

            migrationBuilder.DropIndex(
                name: "ux_customer_contacts_org_portal_user",
                table: "customer_contacts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_contacts_portal_link",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "portal_linked_at",
                table: "customer_contacts");

            migrationBuilder.DropColumn(
                name: "portal_updates_seen_at",
                table: "customer_contacts");
        }
    }
}
