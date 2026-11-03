using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgregarEstadoBulto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Bultos",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pendiente");
            
            migrationBuilder.Sql("""
                                 UPDATE "Bultos" AS b
                                 SET "Estado" = CASE
                                     WHEN e."Estado" = 5 THEN 'Entregado'
                                     WHEN e."Estado" = 9 THEN 'Devuelto'
                                     WHEN e."Estado" = 10 THEN 'Extraviado'
                                     ELSE 'Pendiente'
                                 END
                                 FROM "Envios" AS e
                                 WHERE b."EnvioId" = e."Id";
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Bultos");
        }
    }
}
