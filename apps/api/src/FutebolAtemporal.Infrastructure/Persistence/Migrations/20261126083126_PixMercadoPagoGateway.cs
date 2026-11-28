using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PixMercadoPagoGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentSource",
                table: "MembershipPayments",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchFeePayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    GroupId = table.Column<Guid>(nullable: false),
                    MatchId = table.Column<Guid>(nullable: false),
                    PlayerProfileId = table.Column<Guid>(nullable: false),
                    Amount = table.Column<decimal>(precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(maxLength: 32, nullable: false),
                    PaymentSource = table.Column<string>(maxLength: 32, nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(nullable: true),
                    RecordedByUserId = table.Column<Guid>(nullable: true),
                    Notes = table.Column<string>(maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchFeePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchFeePayments_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchFeePayments_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchFeePayments_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PixCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    GroupId = table.Column<Guid>(nullable: false),
                    PlayerProfileId = table.Column<Guid>(nullable: false),
                    Kind = table.Column<string>(maxLength: 32, nullable: false),
                    MembershipPaymentId = table.Column<Guid>(nullable: true),
                    MatchFeePaymentId = table.Column<Guid>(nullable: true),
                    MatchId = table.Column<Guid>(nullable: true),
                    Amount = table.Column<decimal>(precision: 10, scale: 2, nullable: false),
                    Description = table.Column<string>(maxLength: 200, nullable: false),
                    MpPaymentId = table.Column<string>(maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(maxLength: 64, nullable: false),
                    QrCode = table.Column<string>(type: "text", nullable: false),
                    QrCodeBase64 = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(nullable: false),
                    Status = table.Column<string>(maxLength: 32, nullable: false),
                    ProviderStatus = table.Column<string>(maxLength: 40, nullable: true),
                    IsSimulated = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(nullable: false),
                    ApprovedAt = table.Column<DateTimeOffset>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PixCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PixCharges_FootballGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "FootballGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PixCharges_MatchFeePayments_MatchFeePaymentId",
                        column: x => x.MatchFeePaymentId,
                        principalTable: "MatchFeePayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PixCharges_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PixCharges_MembershipPayments_MembershipPaymentId",
                        column: x => x.MembershipPaymentId,
                        principalTable: "MembershipPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PixCharges_PlayerProfiles_PlayerProfileId",
                        column: x => x.PlayerProfileId,
                        principalTable: "PlayerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchFeePayments_GroupId_Status",
                table: "MatchFeePayments",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchFeePayments_MatchId_PlayerProfileId",
                table: "MatchFeePayments",
                columns: new[] { "MatchId", "PlayerProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchFeePayments_PlayerProfileId",
                table: "MatchFeePayments",
                column: "PlayerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_GroupId_Status_ExpiresAt",
                table: "PixCharges",
                columns: new[] { "GroupId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_IdempotencyKey",
                table: "PixCharges",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_MatchFeePaymentId_Status",
                table: "PixCharges",
                columns: new[] { "MatchFeePaymentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_MatchId",
                table: "PixCharges",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_MembershipPaymentId_Status",
                table: "PixCharges",
                columns: new[] { "MembershipPaymentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_MpPaymentId",
                table: "PixCharges",
                column: "MpPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PixCharges_PlayerProfileId",
                table: "PixCharges",
                column: "PlayerProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PixCharges");

            migrationBuilder.DropTable(
                name: "MatchFeePayments");

            migrationBuilder.DropColumn(
                name: "PaymentSource",
                table: "MembershipPayments");
        }
    }
}
