using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DomainExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PlayerLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Matches_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MembershipBillingPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipBillingPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipBillingPeriods_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyMemberships_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MonthlyMemberships_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PriorityClass = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WaitlistPosition = table.Column<int>(type: "INTEGER", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WithdrawnAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchRegistrations_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchRegistrations_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HomeScore = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayScore = table.Column<int>(type: "INTEGER", nullable: false),
                    WinnerTeamId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchResults_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AwardedByUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAwards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerAwards_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerAwards_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerMatchRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Score = table.Column<decimal>(type: "TEXT", precision: 4, scale: 2, nullable: false),
                    RatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMatchRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerMatchRatings_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerMatchRatings_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamDraws",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Seed = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TeamCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamDraws", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamDraws_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MembershipPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MonthlyMembershipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BillingPeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MembershipPayments_MembershipBillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalTable: "MembershipBillingPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MembershipPayments_MonthlyMemberships_MonthlyMembershipId",
                        column: x => x.MonthlyMembershipId,
                        principalTable: "MonthlyMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TeamDrawId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchTeams_TeamDraws_TeamDrawId",
                        column: x => x.TeamDrawId,
                        principalTable: "TeamDraws",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchTeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchTeamId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SkillRatingSnapshot = table.Column<decimal>(type: "TEXT", precision: 4, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchTeamMembers_MatchTeams_MatchTeamId",
                        column: x => x.MatchTeamId,
                        principalTable: "MatchTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchTeamMembers_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_GroupId_CreatedAt",
                table: "AuditEntries",
                columns: new[] { "GroupId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_GroupId_StartsAt",
                table: "Matches",
                columns: new[] { "GroupId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchRegistrations_MatchId_PlayerProfileId",
                table: "MatchRegistrations",
                columns: new[] { "MatchId", "PlayerProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchRegistrations_MatchId_Status_WaitlistPosition",
                table: "MatchRegistrations",
                columns: new[] { "MatchId", "Status", "WaitlistPosition" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchRegistrations_PlayerProfileId",
                table: "MatchRegistrations",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchResults_MatchId",
                table: "MatchResults",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamMembers_MatchTeamId_PlayerProfileId",
                table: "MatchTeamMembers",
                columns: new[] { "MatchTeamId", "PlayerProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeamMembers_PlayerProfileId",
                table: "MatchTeamMembers",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchTeams_TeamDrawId",
                table: "MatchTeams",
                column: "TeamDrawId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipBillingPeriods_GroupId_Year_Month",
                table: "MembershipBillingPeriods",
                columns: new[] { "GroupId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPayments_BillingPeriodId",
                table: "MembershipPayments",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_MembershipPayments_MonthlyMembershipId_BillingPeriodId",
                table: "MembershipPayments",
                columns: new[] { "MonthlyMembershipId", "BillingPeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyMemberships_GroupId_PlayerProfileId",
                table: "MonthlyMemberships",
                columns: new[] { "GroupId", "PlayerProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyMemberships_PlayerProfileId",
                table: "MonthlyMemberships",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAwards_MatchId_Type",
                table: "PlayerAwards",
                columns: new[] { "MatchId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAwards_PlayerProfileId",
                table: "PlayerAwards",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchRatings_MatchId_PlayerProfileId",
                table: "PlayerMatchRatings",
                columns: new[] { "MatchId", "PlayerProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMatchRatings_PlayerProfileId",
                table: "PlayerMatchRatings",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamDraws_MatchId",
                table: "TeamDraws",
                column: "MatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "MatchRegistrations");

            migrationBuilder.DropTable(
                name: "MatchResults");

            migrationBuilder.DropTable(
                name: "MatchTeamMembers");

            migrationBuilder.DropTable(
                name: "MembershipPayments");

            migrationBuilder.DropTable(
                name: "PlayerAwards");

            migrationBuilder.DropTable(
                name: "PlayerMatchRatings");

            migrationBuilder.DropTable(
                name: "MatchTeams");

            migrationBuilder.DropTable(
                name: "MembershipBillingPeriods");

            migrationBuilder.DropTable(
                name: "MonthlyMemberships");

            migrationBuilder.DropTable(
                name: "TeamDraws");

            migrationBuilder.DropTable(
                name: "Matches");
        }
    }
}
