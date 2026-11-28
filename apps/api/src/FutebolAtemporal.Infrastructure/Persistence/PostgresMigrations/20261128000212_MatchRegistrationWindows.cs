using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.PostgresMigrations
{
    /// <inheritdoc />
    public partial class MatchRegistrationWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CasualRegistrationOpensAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MonthlyRegistrationOpensAt",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId_CreatedAt",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_EntityType_EntityId_CreatedAt",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "CasualRegistrationOpensAt",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "MonthlyRegistrationOpensAt",
                table: "Matches");
        }
    }
}
