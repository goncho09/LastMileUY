using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AislamientoResultadosBultosIntentos : Migration
    {
        /// <inheritdoc />
        
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "ResultadosBultosIntentos"
                                     ENABLE ROW LEVEL SECURITY;

                                 ALTER TABLE "ResultadosBultosIntentos"
                                     FORCE ROW LEVEL SECURITY;

                                 CREATE POLICY aislamiento_operador
                                     ON "ResultadosBultosIntentos"
                                     USING (
                                         EXISTS (
                                             SELECT 1
                                             FROM "IntentosEntrega" i
                                             WHERE i."Id" = "ResultadosBultosIntentos"."IntentoEntregaId"
                                         )
                                     )
                                     WITH CHECK (
                                         EXISTS (
                                             SELECT 1
                                             FROM "IntentosEntrega" i
                                             WHERE i."Id" = "ResultadosBultosIntentos"."IntentoEntregaId"
                                         )
                                     );
                                 """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 DROP POLICY IF EXISTS aislamiento_operador
                                     ON "ResultadosBultosIntentos";

                                 ALTER TABLE "ResultadosBultosIntentos"
                                     NO FORCE ROW LEVEL SECURITY;

                                 ALTER TABLE "ResultadosBultosIntentos"
                                     DISABLE ROW LEVEL SECURITY;
                                 """);
        }

    }
}
