using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WeekGoogleInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "calendar_event_id",
                table: "google_calendar_invites",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "session_week_id",
                table: "google_calendar_invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invites_session_week_id",
                table: "google_calendar_invites",
                column: "session_week_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_google_calendar_invites_one_target",
                table: "google_calendar_invites",
                sql: "(calendar_event_id IS NULL) <> (session_week_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_google_calendar_invites_session_weeks_session_week_id",
                table: "google_calendar_invites",
                column: "session_week_id",
                principalTable: "session_weeks",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_google_calendar_invites_session_weeks_session_week_id",
                table: "google_calendar_invites");

            migrationBuilder.DropIndex(
                name: "ix_google_calendar_invites_session_week_id",
                table: "google_calendar_invites");

            migrationBuilder.DropCheckConstraint(
                name: "ck_google_calendar_invites_one_target",
                table: "google_calendar_invites");

            migrationBuilder.DropColumn(
                name: "session_week_id",
                table: "google_calendar_invites");

            migrationBuilder.AlterColumn<Guid>(
                name: "calendar_event_id",
                table: "google_calendar_invites",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
