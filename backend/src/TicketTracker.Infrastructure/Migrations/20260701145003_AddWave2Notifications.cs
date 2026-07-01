using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWave2Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_notifications_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "activity_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_entries", x => x.Id);
                    table.CheckConstraint("ck_activity_entries_event_type", "event_type IN ('ticket_created','ticket_field_changed','ticket_moved','ticket_assignees_changed','comment_added','comment_edited','comment_deleted','ticket_deleted')");
                    table.ForeignKey(
                        name: "FK_activity_entries_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_activity_entries_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    data_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    emailed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.CheckConstraint("ck_notifications_event_type", "event_type IN ('ticket_created','ticket_field_changed','ticket_moved','ticket_assignees_changed','comment_added','comment_edited','comment_deleted','ticket_deleted')");
                    table.ForeignKey(
                        name: "FK_notifications_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_notifications_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_watchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_watchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_watchers_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_watchers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_entries_actor_id",
                table: "activity_entries",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_ticket_created",
                table: "activity_entries",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_actor_id",
                table: "notifications",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_outbox",
                table: "notifications",
                columns: new[] { "emailed_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_unread",
                table: "notifications",
                columns: new[] { "recipient_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_ticket_id",
                table: "notifications",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_watchers_user_id",
                table: "ticket_watchers",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_ticket_watchers_ticket_user",
                table: "ticket_watchers",
                columns: new[] { "ticket_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_entries");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "ticket_watchers");

            migrationBuilder.DropColumn(
                name: "email_notifications_enabled",
                table: "users");
        }
    }
}
