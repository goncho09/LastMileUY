using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReglaOperativaDelEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReglaOperativaId",
                table: "Envios",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_ReglaOperativaId",
                table: "Envios",
                column: "ReglaOperativaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_ReglasOperativas_ReglaOperativaId",
                table: "Envios",
                column: "ReglaOperativaId",
                principalTable: "ReglasOperativas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                UPDATE "Envios" e
                SET "ReglaOperativaId" = r."Id"
                FROM "ReglasOperativas" r
                WHERE e."ReglaOperativaId" IS NULL
                  AND r."OperadorId" = e."OperadorId"
                  AND r."Modalidad" = e."Modalidad"
                  AND r."Activa";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Envios_ReglasOperativas_ReglaOperativaId",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_ReglaOperativaId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "ReglaOperativaId",
                table: "Envios");
        }
    }
}
