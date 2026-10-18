using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FieldOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTechnicianMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.UniqueConstraint("ak_conversations_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_conversations_job_work_order", "(type = 'job') = (work_order_id IS NOT NULL)");
                    table.CheckConstraint("ck_conversations_type", "type IN ('job', 'team')");
                    table.ForeignKey(
                        name: "fk_conversations_branches_organization_id_branch_id",
                        columns: x => new { x.organization_id, x.branch_id },
                        principalTable: "branches",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_conversations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_conversations_work_orders_organization_id_work_order_id",
                        columns: x => new { x.organization_id, x.work_order_id },
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "id" });
                });

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_messages", x => x.id);
                    table.CheckConstraint("ck_conversation_messages_body", "length(btrim(body)) > 0");
                    table.ForeignKey(
                        name: "fk_conversation_messages_conversations_organization_id_convers",
                        columns: x => new { x.organization_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_conversation_messages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_conversation_messages_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "conversation_read_states",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_read_states", x => new { x.conversation_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_conversation_read_states_conversations_organization_id_conv",
                        columns: x => new { x.organization_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "organization_id", "id" });
                    table.ForeignKey(
                        name: "fk_conversation_read_states_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_conversation_read_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_author_user_id",
                table: "conversation_messages",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_organization_id_conversation_id",
                table: "conversation_messages",
                columns: new[] { "organization_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conversation_messages_client",
                table: "conversation_messages",
                columns: new[] { "conversation_id", "author_user_id", "client_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_read_states_organization_id_conversation_id",
                table: "conversation_read_states",
                columns: new[] { "organization_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_read_states_user_id",
                table: "conversation_read_states",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_org_branch_last",
                table: "conversations",
                columns: new[] { "organization_id", "branch_id", "last_message_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_organization_id_work_order_id",
                table: "conversations",
                columns: new[] { "organization_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ux_conversations_job",
                table: "conversations",
                column: "work_order_id",
                unique: true,
                filter: "type = 'job'");

            migrationBuilder.CreateIndex(
                name: "ux_conversations_team",
                table: "conversations",
                column: "branch_id",
                unique: true,
                filter: "type = 'team'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_messages");

            migrationBuilder.DropTable(
                name: "conversation_read_states");

            migrationBuilder.DropTable(
                name: "conversations");
        }
    }
}
