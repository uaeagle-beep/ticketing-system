using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TicketTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWave2CommentsAndMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "edited_at",
                table: "comments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "comments");
        }
    }
}
