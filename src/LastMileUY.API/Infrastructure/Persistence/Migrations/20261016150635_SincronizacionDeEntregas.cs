using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SincronizacionDeEntregas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperacionesSincronizadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperadorId = table.Column<int>(type: "integer", nullable: false),
                    IdOperacion = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    RutaId = table.Column<int>(type: "integer", nullable: false),
                    EnvioId = table.Column<int>(type: "integer", nullable: false),
                    Resultado = table.Column<int>(type: "integer", nullable: false),
                    Mensaje = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FechaHoraDispositivo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaRecepcion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntentoEntregaId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperacionesSincronizadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperacionesSincronizadas_IntentosEntrega_IntentoEntregaId",
                        column: x => x.IntentoEntregaId,
                        principalTable: "IntentosEntrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OperacionesSincronizadas_Operadores_OperadorId",
                        column: x => x.OperadorId,
                        principalTable: "Operadores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesSincronizadas_IntentoEntregaId",
                table: "OperacionesSincronizadas",
                column: "IntentoEntregaId");

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesSincronizadas_OperadorId_IdOperacion",
                table: "OperacionesSincronizadas",
                columns: new[] { "OperadorId", "IdOperacion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperacionesSincronizadas_RutaId",
                table: "OperacionesSincronizadas",
                column: "RutaId");

            // Aislamiento por operador (ADR-01), igual que el resto de las tablas con OperadorId.
            // Los permisos de lastmileuy_app sobre la tabla nueva salen de los ALTER DEFAULT PRIVILEGES
            // de la migración AislamientoPorOperadorRls.
            migrationBuilder.Sql("""
                ALTER TABLE "OperacionesSincronizadas" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "OperacionesSincronizadas" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "OperacionesSincronizadas"
                    USING ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int)
                    WITH CHECK ("OperadorId" = NULLIF(current_setting('app.operador_id', true), '')::int);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperacionesSincronizadas");
        }
    }
}
