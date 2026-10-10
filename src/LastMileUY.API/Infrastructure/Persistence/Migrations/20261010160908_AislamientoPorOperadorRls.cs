using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    // Segunda barrera del multitenancy (ADR-01): Row-Level Security en PostgreSQL.
    // Aunque una consulta se olvide de filtrar, la base solo devuelve y acepta
    // filas del operador cargado en la variable de conexión app.operador_id.
    public partial class AislamientoPorOperadorRls : Migration
    {
        // Rol con el que se conecta la aplicación. No es dueño de las tablas ni superusuario,
        // así que las políticas RLS se le aplican siempre.
        public const string RolAplicacion = "lastmileuy_app";

        // Tablas con columna OperadorId (entidades que implementan IPerteneceAOperador)
        public static readonly string[] TablasDeOperador =
        [
            "Envios", "Rutas", "Repartidores", "Vehiculos", "Zonas", "ReglasOperativas",
            "CuadrosTarifarios", "Liquidaciones", "MotivosNoEntrega", "SuscripcionesAvisos"
        ];

        // Tablas hijas del envío: se ven solo si se ve su envío
        public static readonly string[] TablasHijasDeEnvio = ["Bultos", "EventosEnvio"];

        // Vacío o sin cargar = ningún operador
        private const string OperadorActual = "NULLIF(current_setting('app.operador_id', true), '')::int";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La contraseña de desarrollo es la misma que la de compose.yaml.
            // En la nube se cambia con ALTER ROLE desde la configuración del servidor, no desde acá.
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
                // USING filtra lo que se lee, modifica o borra; WITH CHECK frena
                // insertar o dejar una fila a nombre de otro operador.
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
                // La subconsulta sobre "Envios" ya pasa por la política de Envios
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

            // El rol se deja: puede tener permisos en otras bases del mismo servidor
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
