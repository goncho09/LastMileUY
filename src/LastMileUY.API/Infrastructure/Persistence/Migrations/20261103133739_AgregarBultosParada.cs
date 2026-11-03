using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarBultosParada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BultosParada",
                columns: table => new
                {
                    ParadaId = table.Column<int>(type: "integer", nullable: false),
                    BultoId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BultosParada", x => new { x.ParadaId, x.BultoId });
                    table.ForeignKey(
                        name: "FK_BultosParada_Bultos_BultoId",
                        column: x => x.BultoId,
                        principalTable: "Bultos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BultosParada_Paradas_ParadaId",
                        column: x => x.ParadaId,
                        principalTable: "Paradas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BultosParada_BultoId",
                table: "BultosParada",
                column: "BultoId");

            // Aislamiento entre operadores (ADR-01): se ve si se ve la parada, que ya pasa por la política
            // de Paradas (y esa por la de Envios). Además, el bulto tiene que ser del envío de esa parada:
            // una parada no puede llevar un bulto de otro envío.
            migrationBuilder.Sql("""
                ALTER TABLE "BultosParada" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "BultosParada" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_operador ON "BultosParada"
                    USING (EXISTS (
                        SELECT 1 FROM "Paradas" p
                        JOIN "Bultos" b ON b."EnvioId" = p."EnvioId"
                        WHERE p."Id" = "BultosParada"."ParadaId" AND b."Id" = "BultosParada"."BultoId"))
                    WITH CHECK (EXISTS (
                        SELECT 1 FROM "Paradas" p
                        JOIN "Bultos" b ON b."EnvioId" = p."EnvioId"
                        WHERE p."Id" = "BultosParada"."ParadaId" AND b."Id" = "BultosParada"."BultoId"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La política se va con la tabla
            migrationBuilder.DropTable(
                name: "BultosParada");
        }
    }
}
