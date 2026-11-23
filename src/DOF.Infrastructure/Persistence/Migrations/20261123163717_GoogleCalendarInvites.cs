using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GoogleCalendarInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "google_calendar_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    encrypted_refresh_token = table.Column<string>(type: "text", nullable: false),
                    calendar_id = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    connected_by = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    last_error_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_calendar_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_google_calendar_connections_users_connected_by",
                        column: x => x.connected_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "google_calendar_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    google_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    synced_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_in_google = table.Column<bool>(type: "boolean", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responses_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_calendar_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_google_calendar_invites_calendar_events_calendar_event_id",
                        column: x => x.calendar_event_id,
                        principalTable: "calendar_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_google_calendar_invites_google_calendar_connections_connect",
                        column: x => x.connection_id,
                        principalTable: "google_calendar_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_google_calendar_invites_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_google_calendar_invites_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "google_calendar_invite_guests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    response = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_google_calendar_invite_guests", x => x.id);
                    table.ForeignKey(
                        name: "fk_google_calendar_invite_guests_google_calendar_invites_invit",
                        column: x => x.invite_id,
                        principalTable: "google_calendar_invites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_google_calendar_invite_guests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_connections_connected_by",
                table: "google_calendar_connections",
                column: "connected_by");

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_connections_single_active",
                table: "google_calendar_connections",
                column: "archived_at",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invite_guests_invite_id_email",
                table: "google_calendar_invite_guests",
                columns: new[] { "invite_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invite_guests_user_id",
                table: "google_calendar_invite_guests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invites_calendar_event_id",
                table: "google_calendar_invites",
                column: "calendar_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invites_connection_id",
                table: "google_calendar_invites",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invites_created_by",
                table: "google_calendar_invites",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invites_status_next_attempt_at",
                table: "google_calendar_invites",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_google_calendar_invites_updated_by",
                table: "google_calendar_invites",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "google_calendar_invite_guests");

            migrationBuilder.DropTable(
                name: "google_calendar_invites");

            migrationBuilder.DropTable(
                name: "google_calendar_connections");
        }
    }
}
