using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionWeeks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "session_week_id",
                table: "calendar_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "speaker",
                table: "calendar_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "theme",
                table: "calendar_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "session_weeks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    meeting_url = table.Column<string>(type: "text", nullable: true),
                    speaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_weeks", x => x.id);
                    table.CheckConstraint("ck_session_weeks_starts_monday", "EXTRACT(ISODOW FROM week_start) = 1");
                    table.ForeignKey(
                        name: "fk_session_weeks_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_weeks_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_session_week_id",
                table: "calendar_events",
                column: "session_week_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_weeks_created_by",
                table: "session_weeks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_session_weeks_name_week_start",
                table: "session_weeks",
                columns: new[] { "name", "week_start" });

            migrationBuilder.CreateIndex(
                name: "ix_session_weeks_updated_by",
                table: "session_weeks",
                column: "updated_by");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_events_session_weeks_session_week_id",
                table: "calendar_events",
                column: "session_week_id",
                principalTable: "session_weeks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_calendar_events_session_weeks_session_week_id",
                table: "calendar_events");

            migrationBuilder.DropTable(
                name: "session_weeks");

            migrationBuilder.DropIndex(
                name: "ix_calendar_events_session_week_id",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "session_week_id",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "speaker",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "theme",
                table: "calendar_events");
        }
    }
}
