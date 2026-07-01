using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWave3Attachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_event_type",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_activity_entries_event_type",
                table: "activity_entries");

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    original_filename = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_attachments_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attachments_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_event_type",
                table: "notifications",
                sql: "event_type IN ('ticket_created','ticket_field_changed','ticket_moved','ticket_assignees_changed','comment_added','comment_edited','comment_deleted','ticket_deleted','attachment_added','attachment_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_activity_entries_event_type",
                table: "activity_entries",
                sql: "event_type IN ('ticket_created','ticket_field_changed','ticket_moved','ticket_assignees_changed','comment_added','comment_edited','comment_deleted','ticket_deleted','attachment_added','attachment_deleted')");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_ticket_created",
                table: "attachments",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_uploaded_by",
                table: "attachments",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ux_attachments_storage_key",
                table: "attachments",
                column: "storage_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_event_type",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_activity_entries_event_type",
                table: "activity_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_event_type",
                table: "notifications",
                sql: "event_type IN ('ticket_created','ticket_field_changed','ticket_moved','ticket_assignees_changed','comment_added','comment_edited','comment_deleted','ticket_deleted')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_activity_entries_event_type",
                table: "activity_entries",
                sql: "event_type IN ('ticket_created','ticket_field_changed','ticket_moved','ticket_assignees_changed','comment_added','comment_edited','comment_deleted','ticket_deleted')");
        }
    }
}
