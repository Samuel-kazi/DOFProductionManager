using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepeatingEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "series_id",
                table: "calendar_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "event_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    weekdays = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    until = table.Column<DateOnly>(type: "date", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_series", x => x.id);
                    table.CheckConstraint("ck_event_series_interval", "interval BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_event_series_one_end", "(until IS NULL) <> (count IS NULL)");
                    table.ForeignKey(
                        name: "fk_event_series_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_series_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_series_id",
                table: "calendar_events",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_series_created_by",
                table: "event_series",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_event_series_updated_by",
                table: "event_series",
                column: "updated_by");

            migrationBuilder.AddForeignKey(
                name: "fk_calendar_events_event_series_series_id",
                table: "calendar_events",
                column: "series_id",
                principalTable: "event_series",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_calendar_events_event_series_series_id",
                table: "calendar_events");

            migrationBuilder.DropTable(
                name: "event_series");

            migrationBuilder.DropIndex(
                name: "ix_calendar_events_series_id",
                table: "calendar_events");

            migrationBuilder.DropColumn(
                name: "series_id",
                table: "calendar_events");
        }
    }
}
