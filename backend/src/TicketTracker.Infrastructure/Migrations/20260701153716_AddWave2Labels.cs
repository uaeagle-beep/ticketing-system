using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWave2Labels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_labels_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_labels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_labels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_labels_labels_label_id",
                        column: x => x.label_id,
                        principalTable: "labels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_labels_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_labels_team_name",
                table: "labels",
                columns: new[] { "team_id", "name_normalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_labels_label_id",
                table: "ticket_labels",
                column: "label_id");

            migrationBuilder.CreateIndex(
                name: "ux_ticket_labels_ticket_label",
                table: "ticket_labels",
                columns: new[] { "ticket_id", "label_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_labels");

            migrationBuilder.DropTable(
                name: "labels");
        }
    }
}
