using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCorreosSeguimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CorreosSeguimiento",
                columns: table => new
                {
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<int>(type: "integer", nullable: false),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    Estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    FechaEnvio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorreosSeguimiento", x => x.EventoId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CorreosSeguimiento_OperadorId_ComercioId_Estado",
                table: "CorreosSeguimiento",
                columns: new[] { "OperadorId", "ComercioId", "Estado" });



            migrationBuilder.Sql("""
                                 ALTER TABLE "CorreosSeguimiento"
                                     ENABLE ROW LEVEL SECURITY;

                                 ALTER TABLE "CorreosSeguimiento"
                                     FORCE ROW LEVEL SECURITY;

                                 CREATE POLICY aislamiento_correos
                                 ON "CorreosSeguimiento"
                                 FOR ALL
                                 USING (
                                     "OperadorId" =
                                         NULLIF(current_setting('app.operador_id', true), '')::integer
                                     AND
                                     "ComercioId" =
                                         NULLIF(current_setting('app.comercio_id', true), '')::integer
                                 )
                                 WITH CHECK (
                                     "OperadorId" =
                                         NULLIF(current_setting('app.operador_id', true), '')::integer
                                     AND
                                     "ComercioId" =
                                         NULLIF(current_setting('app.comercio_id', true), '')::integer
                                 );
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorreosSeguimiento");
        }
    }
}
