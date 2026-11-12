using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AmbientePruebasApiPublica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EsAmbientePruebas",
                table: "Operadores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EsPrueba",
                table: "Destinatarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EsPrueba",
                table: "ClavesApi",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Operadores_UnoDePruebas",
                table: "Operadores",
                column: "EsAmbientePruebas",
                unique: true,
                filter: "\"EsAmbientePruebas\"");

            // Ambiente de pruebas de la API pública (docs/api-publica.md): los destinatarios cargados con una
            // clave de prueba solo se ven con una clave de prueba, y los de verdad solo fuera de ese ambiente.
            // AS RESTRICTIVE: se suma (AND) a aislamiento_comercio, que no se toca.
            // Sin la variable cargada (cualquier conexión que no sea de un pedido con clave de prueba) vale
            // como producción: se ven los de verdad, como hasta ahora. Las direcciones siguen a su destinatario.
            migrationBuilder.Sql("""
                CREATE POLICY ambiente_destinatarios ON "Destinatarios" AS RESTRICTIVE
                    USING ("EsPrueba" = (COALESCE(current_setting('app.ambiente_pruebas', true), '') = '1'))
                    WITH CHECK ("EsPrueba" = (COALESCE(current_setting('app.ambiente_pruebas', true), '') = '1'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP POLICY IF EXISTS ambiente_destinatarios ON "Destinatarios";""");

            migrationBuilder.DropIndex(
                name: "IX_Operadores_UnoDePruebas",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "EsAmbientePruebas",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "EsPrueba",
                table: "Destinatarios");

            migrationBuilder.DropColumn(
                name: "EsPrueba",
                table: "ClavesApi");
        }
    }
}
