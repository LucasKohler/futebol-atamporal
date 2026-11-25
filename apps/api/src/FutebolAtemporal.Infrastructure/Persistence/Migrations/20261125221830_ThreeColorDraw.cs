using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ThreeColorDraw : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlgorithmVersion",
                table: "TeamDraws",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanPlayGoalkeeperSnapshot",
                table: "MatchTeamMembers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "GoalkeeperRatingSnapshot",
                table: "MatchTeamMembers",
                type: "TEXT",
                precision: 4,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "MatchTeamMembers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlgorithmVersion",
                table: "TeamDraws");

            migrationBuilder.DropColumn(
                name: "CanPlayGoalkeeperSnapshot",
                table: "MatchTeamMembers");

            migrationBuilder.DropColumn(
                name: "GoalkeeperRatingSnapshot",
                table: "MatchTeamMembers");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "MatchTeamMembers");
        }
    }
}
