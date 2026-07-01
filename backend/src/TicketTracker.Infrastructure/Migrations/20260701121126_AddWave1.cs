using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWave1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "tickets",
                type: "date",
                nullable: true);

            // Backfill existing rows to 'medium' at add-time (satisfies NOT NULL and the CHECK below).
            // This default is migration-only; the model carries NO store default so the parity guard
            // (has-pending-model-changes) stays clean (WAVE1_DESIGN §3.1, R-2).
            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "tickets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "medium");

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_assignees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_assignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_assignees_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_assignees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tickets_priority",
                table: "tickets",
                sql: "priority IN ('low','medium','high','urgent')");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_user_id",
                table: "password_reset_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_ticket_assignees_user_id",
                table: "ticket_assignees",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_ticket_assignees_ticket_user",
                table: "ticket_assignees",
                columns: new[] { "ticket_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropTable(
                name: "ticket_assignees");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tickets_priority",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "tickets");
        }
    }
}
