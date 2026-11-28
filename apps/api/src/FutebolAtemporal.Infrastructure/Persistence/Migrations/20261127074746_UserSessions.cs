using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FutebolAtemporal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    UserId = table.Column<Guid>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(nullable: true),
                    SecurityStamp = table.Column<string>(maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");

            // Inscrição push vinculada à sessão que a gravou (nula = não recebe push).
            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "PushSubscriptions",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushSubscriptions_SessionId",
                table: "PushSubscriptions",
                column: "SessionId");

            // Corte de versão: troca o carimbo de segurança de todos os usuários. Cookies v1 (emitidos pela
            // versão antiga) deixam de valer também na versão antiga, então um rollback não ressuscita sessões
            // anteriores ao deploy. Efeito: todos precisam entrar de novo (o cookie v2 já exigiria isso).
            // Valor aleatório por linha (o carimbo também é chave de tokens do Identity: não pode ser previsível).
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = upper(replace(gen_random_uuid()::text, '-', ''));");
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
                migrationBuilder.Sql("UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = upper(hex(randomblob(16)));");
            else
                throw new NotSupportedException($"Provedor sem rotação de carimbo definida: {migrationBuilder.ActiveProvider}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não restaura carimbos (impossível e indesejado). Rollback do app NÃO precisa rodar este Down:
            // a versão antiga não lê cookies v2, então não depende das revogações guardadas aqui.
            migrationBuilder.DropIndex(
                name: "IX_PushSubscriptions_SessionId",
                table: "PushSubscriptions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "PushSubscriptions");

            migrationBuilder.DropTable(
                name: "UserSessions");
        }
    }
}
