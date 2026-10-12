using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    // Completa la segunda barrera del multitenancy (ADR-01):
    // 1. Tablas hijas sin OperadorId: una fila se ve solo si se ve su "padre".
    // 2. Segundo nivel (comercio): un usuario de comercio solo ve lo de su comercio.
    // Los filtros de EF en ApplicationDbContext siguen el mismo criterio.
    public partial class AislamientoCompleto : Migration
    {
        // Vacío o sin cargar = ninguno
        private const string OperadorActual = "NULLIF(current_setting('app.operador_id', true), '')::int";
        private const string ComercioActual = "NULLIF(current_setting('app.comercio_id', true), '')::int";

        // Tabla hija, columna que apunta al padre y tabla padre. La subconsulta sobre el padre
        // pasa por la política del padre, así que el aislamiento se encadena (PruebasEntrega → IntentosEntrega → Envios).
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

        // Tablas con OperadorId y ComercioId: además del operador, se filtran por comercio
        public static readonly string[] TablasDeComercio = ["Envios", "Liquidaciones", "SuscripcionesAvisos"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Destinatarios: son del comercio, no del operador (no tienen OperadorId).
            // Un usuario de comercio ve los suyos; un usuario del operador, los de los comercios con los que trabaja.
            // Va antes que Direcciones, que depende de esta política.
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

            // AS RESTRICTIVE: se suma (AND) a la política del operador que ya tienen estas tablas.
            // Sin comercio cargado (usuario del operador) no restringe nada.
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
