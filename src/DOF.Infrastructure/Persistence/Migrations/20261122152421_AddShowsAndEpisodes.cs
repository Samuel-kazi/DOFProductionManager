using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShowsAndEpisodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "episode_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    producer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    show_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    default_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shows", x => x.id);
                    table.ForeignKey(
                        name: "fk_shows_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shows_users_producer_id",
                        column: x => x.producer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shows_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    show_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seasons", x => x.id);
                    table.ForeignKey(
                        name: "fk_seasons_shows_show_id",
                        column: x => x.show_id,
                        principalTable: "shows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seasons_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seasons_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "episodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    show_id = table.Column<Guid>(type: "uuid", nullable: false),
                    season_id = table.Column<Guid>(type: "uuid", nullable: true),
                    episode_number = table.Column<int>(type: "integer", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    producer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recording_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recording_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recording_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    editing_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    review_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    approval_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_episodes", x => x.id);
                    table.CheckConstraint("ck_episodes_episode_number", "episode_number IS NULL OR episode_number > 0");
                    table.CheckConstraint("ck_episodes_recording_range", "(recording_start IS NULL AND recording_end IS NULL) OR (recording_start IS NOT NULL AND recording_end > recording_start)");
                    table.ForeignKey(
                        name: "fk_episodes_seasons_season_id",
                        column: x => x.season_id,
                        principalTable: "seasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_episodes_shows_show_id",
                        column: x => x.show_id,
                        principalTable: "shows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_episodes_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_episodes_users_producer_id",
                        column: x => x.producer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_episodes_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "episode_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsibility = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_episode_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_episode_assignments_episodes_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_episode_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tasks_episode_id",
                table: "tasks",
                column: "episode_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tasks_project_or_episode",
                table: "tasks",
                sql: "project_id IS NULL OR episode_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_episode_assignments_episode_id_user_id_responsibility",
                table: "episode_assignments",
                columns: new[] { "episode_id", "user_id", "responsibility" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_episode_assignments_user_id",
                table: "episode_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_approval_due_date",
                table: "episodes",
                column: "approval_due_date");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_created_by",
                table: "episodes",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_editing_due_date",
                table: "episodes",
                column: "editing_due_date");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_producer_id",
                table: "episodes",
                column: "producer_id");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_recording_start",
                table: "episodes",
                column: "recording_start");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_review_due_date",
                table: "episodes",
                column: "review_due_date");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_season_id_episode_number",
                table: "episodes",
                columns: new[] { "season_id", "episode_number" },
                unique: true,
                filter: "season_id IS NOT NULL AND episode_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_show_id",
                table: "episodes",
                column: "show_id");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_show_id_episode_number_without_season",
                table: "episodes",
                columns: new[] { "show_id", "episode_number" },
                unique: true,
                filter: "season_id IS NULL AND episode_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_status",
                table: "episodes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_episodes_updated_by",
                table: "episodes",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_seasons_created_by",
                table: "seasons",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_seasons_show_id_season_number",
                table: "seasons",
                columns: new[] { "show_id", "season_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seasons_updated_by",
                table: "seasons",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_shows_created_by",
                table: "shows",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_shows_producer_id",
                table: "shows",
                column: "producer_id");

            migrationBuilder.CreateIndex(
                name: "ix_shows_status",
                table: "shows",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_shows_updated_by",
                table: "shows",
                column: "updated_by");

            migrationBuilder.AddForeignKey(
                name: "fk_tasks_episodes_episode_id",
                table: "tasks",
                column: "episode_id",
                principalTable: "episodes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tasks_episodes_episode_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "episode_assignments");

            migrationBuilder.DropTable(
                name: "episodes");

            migrationBuilder.DropTable(
                name: "seasons");

            migrationBuilder.DropTable(
                name: "shows");

            migrationBuilder.DropIndex(
                name: "ix_tasks_episode_id",
                table: "tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tasks_project_or_episode",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "episode_id",
                table: "tasks");
        }
    }
}
