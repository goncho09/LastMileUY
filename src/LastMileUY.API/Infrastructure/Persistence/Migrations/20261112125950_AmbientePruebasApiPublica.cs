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
