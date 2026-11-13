using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    public partial class AislamientoCompleto : Migration
    {
        private const string OperadorActual = "NULLIF(current_setting('app.operador_id', true), '')::int";
        private const string ComercioActual = "NULLIF(current_setting('app.comercio_id', true), '')::int";

        public static readonly (string Tabla, string Columna, string Padre)[] TablasHijas =
        [
            ("IntentosEntrega", "EnvioId", "Envios"),
            ("Paradas", "EnvioId", "Envios"),
            ("Devoluciones", "EnvioId", "Envios"),
            ("Notificaciones", "EnvioId", "Envios"),
            ("EntregasAvisos", "EnvioId", "Envios"),
            ("PruebasEntrega", "IntentoEntregaId", "IntentosEntrega"),
            ("Incidencias", "RutaId", "Rutas"),
            ("Posiciones", "RutaId", "Rutas"),
            ("Rendiciones", "RutaId", "Rutas"),
            ("Tarifas", "CuadroTarifarioId", "CuadrosTarifarios"),
            ("AjustesTarifa", "TarifaId", "Tarifas"),
            ("DetallesLiquidacion", "LiquidacionId", "Liquidaciones"),
            ("FranjasHorarias", "ZonaId", "Zonas"),
            ("Direcciones", "DestinatarioId", "Destinatarios")
        ];

        public static readonly string[] TablasDeComercio = ["Envios", "Liquidaciones", "SuscripcionesAvisos"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE "Destinatarios" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "Destinatarios" FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_comercio ON "Destinatarios"
                    USING (CASE WHEN {ComercioActual} IS NOT NULL
                        THEN "ComercioId" = {ComercioActual}
                        ELSE EXISTS (SELECT 1 FROM "OperadoresComercios" oc
                                     WHERE oc."ComercioId" = "Destinatarios"."ComercioId"
                                       AND oc."OperadorId" = {OperadorActual}) END)
                    WITH CHECK (CASE WHEN {ComercioActual} IS NOT NULL
                        THEN "ComercioId" = {ComercioActual}
                        ELSE EXISTS (SELECT 1 FROM "OperadoresComercios" oc
                                     WHERE oc."ComercioId" = "Destinatarios"."ComercioId"
                                       AND oc."OperadorId" = {OperadorActual}) END);
                """);

            foreach (var (tabla, columna, padre) in TablasHijas)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE "{tabla}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{tabla}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_operador ON "{tabla}"
                        USING (EXISTS (SELECT 1 FROM "{padre}" p WHERE p."Id" = "{tabla}"."{columna}"))
                        WITH CHECK (EXISTS (SELECT 1 FROM "{padre}" p WHERE p."Id" = "{tabla}"."{columna}"));
                    """);
            }

            foreach (var tabla in TablasDeComercio)
            {
                migrationBuilder.Sql($"""
                    CREATE POLICY aislamiento_comercio ON "{tabla}" AS RESTRICTIVE
                        USING ({ComercioActual} IS NULL OR "ComercioId" = {ComercioActual})
                        WITH CHECK ({ComercioActual} IS NULL OR "ComercioId" = {ComercioActual});
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in TablasDeComercio)
            {
                migrationBuilder.Sql($"""DROP POLICY IF EXISTS aislamiento_comercio ON "{tabla}";""");
            }

            foreach (var (tabla, _, _) in Enumerable.Reverse(TablasHijas))
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS aislamiento_operador ON "{tabla}";
                    ALTER TABLE "{tabla}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE "{tabla}" DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS aislamiento_comercio ON "Destinatarios";
                ALTER TABLE "Destinatarios" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE "Destinatarios" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }
}
