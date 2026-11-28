using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlayerGoalkeeperRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanPlayGoalkeeper",
                table: "PlayerProfiles",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "GoalkeeperRating",
                table: "PlayerProfiles",
                precision: 4,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanPlayGoalkeeper",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "GoalkeeperRating",
                table: "PlayerProfiles");
        }
    }
}
