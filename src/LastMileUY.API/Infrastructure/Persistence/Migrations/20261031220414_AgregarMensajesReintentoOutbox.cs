using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarMensajesReintentoOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MensajesReintentoOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MensajeFallidoId = table.Column<Guid>(type: "uuid", nullable: false),
                    MensajeId = table.Column<string>(type: "text", nullable: false),
                    Contenido = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<string>(type: "text", nullable: true),
                    CorrelacionId = table.Column<string>(type: "text", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaPublicacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntentosPublicacion = table.Column<int>(type: "integer", nullable: false),
                    UltimoError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesReintentoOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensajesReintentoOutbox_MensajesFallidos_MensajeFallidoId",
                        column: x => x.MensajeFallidoId,
                        principalTable: "MensajesFallidos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesReintentoOutbox_FechaCreacion",
                table: "MensajesReintentoOutbox",
                column: "FechaCreacion",
                filter: "\"FechaPublicacion\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MensajesReintentoOutbox_MensajeFallidoId",
                table: "MensajesReintentoOutbox",
                column: "MensajeFallidoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MensajesReintentoOutbox");
        }
    }
}
