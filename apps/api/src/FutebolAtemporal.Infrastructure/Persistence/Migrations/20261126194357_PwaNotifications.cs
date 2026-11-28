using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PwaNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    GroupId = table.Column<Guid>(nullable: false),
                    Source = table.Column<string>(maxLength: 32, nullable: false),
                    Category = table.Column<string>(maxLength: 32, nullable: false),
                    Audience = table.Column<string>(maxLength: 32, nullable: false),
                    Title = table.Column<string>(maxLength: 120, nullable: false),
                    Body = table.Column<string>(maxLength: 500, nullable: false),
                    ActionUrl = table.Column<string>(maxLength: 64, nullable: false),
                    MatchId = table.Column<Guid>(nullable: true),
                    CreatedByUserId = table.Column<Guid>(nullable: true),
                    IdempotencyKey = table.Column<string>(maxLength: 64, nullable: true),
                    RequestHash = table.Column<string>(maxLength: 64, nullable: true),
                    RecipientCount = table.Column<int>(nullable: false),
                    PushDeviceCount = table.Column<int>(nullable: false),
                    PushSentCount = table.Column<int>(nullable: false),
                    PushFailedCount = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationCampaigns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationCampaigns_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    Endpoint = table.Column<string>(maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(maxLength: 256, nullable: false),
                    Auth = table.Column<string>(maxLength: 128, nullable: false),
                    VapidKeyId = table.Column<string>(maxLength: 16, nullable: false),
                    UserAgent = table.Column<string>(maxLength: 256, nullable: true),
                    DeviceLabel = table.Column<string>(maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(nullable: false),
                    LastSuccessAt = table.Column<DateTimeOffset>(nullable: true),
                    FailureCount = table.Column<int>(nullable: false),
                    FailureReason = table.Column<string>(maxLength: 200, nullable: true),
                    DeactivatedAt = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    GroupId = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    CampaignId = table.Column<Guid>(nullable: false),
                    Category = table.Column<string>(maxLength: 32, nullable: false),
                    Title = table.Column<string>(maxLength: 120, nullable: false),
                    Body = table.Column<string>(maxLength: 500, nullable: false),
                    ActionUrl = table.Column<string>(maxLength: 64, nullable: false),
                    MatchId = table.Column<Guid>(nullable: true),
                    DedupeKey = table.Column<string>(maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(nullable: true),
                    PushState = table.Column<string>(maxLength: 32, nullable: false),
                    PushAttempts = table.Column<int>(nullable: false),
                    NextPushAt = table.Column<DateTimeOffset>(nullable: true),
                    PushSentCount = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserNotifications_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNotifications_NotificationCampaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "NotificationCampaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCampaigns_GroupId_IdempotencyKey",
                table: "NotificationCampaigns",
                columns: new[] { "GroupId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationCampaigns_GroupId_Source_Category_CreatedAt",
                table: "NotificationCampaigns",
                columns: new[] { "GroupId", "Source", "Category", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_Endpoint",
                table: "PushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_UserId_IsActive",
                table: "PushSubscriptions",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_CampaignId",
                table: "UserNotifications",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_GroupId",
                table: "UserNotifications",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_PushState_NextPushAt",
                table: "UserNotifications",
                columns: new[] { "PushState", "NextPushAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_DedupeKey",
                table: "UserNotifications",
                columns: new[] { "UserId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_UserId_GroupId_ReadAt",
                table: "UserNotifications",
                columns: new[] { "UserId", "GroupId", "ReadAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "NotificationCampaigns");
        }
    }
}
