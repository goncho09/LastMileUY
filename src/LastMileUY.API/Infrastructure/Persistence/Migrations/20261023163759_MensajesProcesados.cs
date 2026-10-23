using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MensajesProcesados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MensajesProcesados",
                columns: table => new
                {
                    MensajeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumidor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    FechaProcesado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesProcesados", x => new { x.MensajeId, x.Consumidor });
                });

            // Como toda tabla de un operador (ADR-01). El worker carga el operador de cada evento antes
            // de procesarlo, así que solo ve y anota los de ese operador.
            migrationBuilder.Sql("""
                ALTER TABLE "MensajesProcesados" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "MensajesProcesados" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "MensajesProcesados"
                    USING ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int)
                    WITH CHECK ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS aislamiento_operador ON "MensajesProcesados";""");

            migrationBuilder.DropTable(
                name: "MensajesProcesados");
        }
    }
}
