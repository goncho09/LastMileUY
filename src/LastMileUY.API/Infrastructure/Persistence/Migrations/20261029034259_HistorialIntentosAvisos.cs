using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistorialIntentosAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntentosAvisos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntregaAvisoId = table.Column<int>(type: "integer", nullable: false),
                    NumeroIntento = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Exitoso = table.Column<bool>(type: "boolean", nullable: false),
                    CodigoHttp = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DuracionMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentosAvisos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntentosAvisos_EntregasAvisos_EntregaAvisoId",
                        column: x => x.EntregaAvisoId,
                        principalTable: "EntregasAvisos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntentosAvisos_EntregaAvisoId_NumeroIntento",
                table: "IntentosAvisos",
                columns: new[] { "EntregaAvisoId", "NumeroIntento" },
                unique: true);

            // Como las demás tablas hijas (ADR-01, migración AislamientoCompleto): un intento se ve solo si se
            // ve su aviso, que a su vez se ve solo si se ve su envío. El worker carga el operador antes de
            // anotar el intento, así que pasa el WITH CHECK. Los permisos de lastmileuy_app salen de los
            // ALTER DEFAULT PRIVILEGES.
            migrationBuilder.Sql("""
                ALTER TABLE "IntentosAvisos" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "IntentosAvisos" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "IntentosAvisos"
                    USING (EXISTS (SELECT 1 FROM "EntregasAvisos" p WHERE p."Id" = "IntentosAvisos"."EntregaAvisoId"))
                    WITH CHECK (EXISTS (SELECT 1 FROM "EntregasAvisos" p WHERE p."Id" = "IntentosAvisos"."EntregaAvisoId"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS aislamiento_operador ON "IntentosAvisos";""");

            migrationBuilder.DropTable(
                name: "IntentosAvisos");
        }
    }
}
