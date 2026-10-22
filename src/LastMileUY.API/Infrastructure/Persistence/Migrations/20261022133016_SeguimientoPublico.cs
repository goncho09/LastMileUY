using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    // Seguimiento público: el destinatario consulta su envío con el código, sin sesión (sin operador).
    // Las políticas de AislamientoPorOperadorRls no le dejan ver nada, así que se suma esta, que se
    // combina con OR con la del operador: deja LEER (solo SELECT) el envío cuyo código está cargado en
    // app.codigo_seguimiento. La carga SeguimientoPublicoService dentro de una transacción.
    // Los eventos del envío (EventosEnvio) se ven por su política de siempre: se ve el padre.
    public partial class SeguimientoPublico : Migration
    {
        // Vacío o sin cargar = ninguno
        private const string CodigoActual = "NULLIF(current_setting('app.codigo_seguimiento', true), '')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                CREATE POLICY seguimiento_publico ON "Envios"
                    FOR SELECT
                    USING ("CodigoSeguimiento" = {CodigoActual});
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS seguimiento_publico ON "Envios";""");
        }
    }
}
