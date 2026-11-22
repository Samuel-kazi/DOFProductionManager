using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "distribution_channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_distribution_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_distribution_channels_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_distribution_channels_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "releases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    episode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    distribution_channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    actual_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    external_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    postponement_reason = table.Column<string>(type: "text", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    published_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_releases", x => x.id);
                    table.CheckConstraint("ck_releases_published_has_time", "status <> 'Published' OR actual_published_at IS NOT NULL");
                    table.CheckConstraint("ck_releases_status_not_overdue", "status <> 'Overdue'");
                    table.ForeignKey(
                        name: "fk_releases_distribution_channels_distribution_channel_id",
                        column: x => x.distribution_channel_id,
                        principalTable: "distribution_channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_releases_episodes_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_releases_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_releases_users_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_releases_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_releases_users_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_releases_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_distribution_channels_created_by",
                table: "distribution_channels",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_distribution_channels_name",
                table: "distribution_channels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_distribution_channels_updated_by",
                table: "distribution_channels",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_releases_assigned_to",
                table: "releases",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_releases_created_by",
                table: "releases",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_releases_distribution_channel_id",
                table: "releases",
                column: "distribution_channel_id");

            migrationBuilder.CreateIndex(
                name: "ix_releases_episode_id",
                table: "releases",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "ix_releases_episode_id_distribution_channel_id",
                table: "releases",
                columns: new[] { "episode_id", "distribution_channel_id" },
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "ix_releases_project_id",
                table: "releases",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_releases_published_by",
                table: "releases",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "ix_releases_scheduled_at",
                table: "releases",
                column: "scheduled_at");

            migrationBuilder.CreateIndex(
                name: "ix_releases_status",
                table: "releases",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_releases_updated_by",
                table: "releases",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "releases");

            migrationBuilder.DropTable(
                name: "distribution_channels");
        }
    }
}
