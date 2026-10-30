using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarMensajesFallidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MensajesFallidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MensajeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CorrelacionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Contenido = table.Column<string>(type: "text", nullable: false),
                    UltimoError = table.Column<string>(type: "text", nullable: true),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    FechaFallo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FechaSolicitudReproceso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SolicitadoPorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    FechaResolucion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesFallidos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesFallidos_Estado_FechaFallo",
                table: "MensajesFallidos",
                columns: new[] { "Estado", "FechaFallo" });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesFallidos_MensajeId",
                table: "MensajesFallidos",
                column: "MensajeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MensajesFallidos");
        }
    }
}
