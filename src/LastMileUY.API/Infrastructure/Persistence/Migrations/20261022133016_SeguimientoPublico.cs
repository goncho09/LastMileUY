using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    public partial class SeguimientoPublico : Migration
    {
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
