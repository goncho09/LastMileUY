using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    // ADR-01: segunda barrera del multitenancy. La base filtra por app.operador_id aunque una consulta no lo haga.
    public partial class AislamientoPorOperadorRls : Migration
    {
        public const string RolAplicacion = "lastmileuy_app";

        public static readonly string[] TablasDeOperador =
        [
            "Envios", "Rutas", "Repartidores", "Vehiculos", "Zonas", "ReglasOperativas",
            "CuadrosTarifarios", "Liquidaciones", "MotivosNoEntrega", "SuscripcionesAvisos"
        ];

        public static readonly string[] TablasHijasDeEnvio = ["Bultos", "EventosEnvio"];

        private const string OperadorActual = "NULLIF(current_setting('app.operador_id', true), '')::int";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{RolAplicacion}') THEN
                        CREATE ROLE {RolAplicacion} LOGIN PASSWORD 'lastmileuy_app_dev'
                            NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
                    END IF;
                END
                $$;

                GRANT USAGE ON SCHEMA public TO {RolAplicacion};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {RolAplicacion};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {RolAplicacion};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {RolAplicacion};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO {RolAplicacion};
                """);

            foreach (var tabla in TablasDeOperador)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE "{tabla}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{tabla}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_operador ON "{tabla}"
                        USING ("OperadorId" = {OperadorActual})
                        WITH CHECK ("OperadorId" = {OperadorActual});
                    """);
            }

            foreach (var tabla in TablasHijasDeEnvio)
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE "{tabla}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{tabla}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_operador ON "{tabla}"
                        USING (EXISTS (SELECT 1 FROM "Envios" e WHERE e."Id" = "EnvioId"))
                        WITH CHECK (EXISTS (SELECT 1 FROM "Envios" e WHERE e."Id" = "EnvioId"));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in TablasDeOperador.Concat(TablasHijasDeEnvio))
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS aislamiento_operador ON "{tabla}";
                    ALTER TABLE "{tabla}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE "{tabla}" DISABLE ROW LEVEL SECURITY;
                    """);
            }

            migrationBuilder.Sql($"""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM {RolAplicacion};
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE USAGE, SELECT ON SEQUENCES FROM {RolAplicacion};
                REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {RolAplicacion};
                REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {RolAplicacion};
                REVOKE USAGE ON SCHEMA public FROM {RolAplicacion};
                """);
        }
    }
}
