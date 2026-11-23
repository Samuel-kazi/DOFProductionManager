using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DOF.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleasesWithoutEpisode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "episode_id",
                table: "releases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "releases",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_releases_episode_or_title",
                table: "releases",
                sql: "episode_id IS NOT NULL OR title IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_releases_episode_or_title",
                table: "releases");

            migrationBuilder.DropColumn(
                name: "title",
                table: "releases");

            migrationBuilder.AlterColumn<Guid>(
                name: "episode_id",
                table: "releases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
