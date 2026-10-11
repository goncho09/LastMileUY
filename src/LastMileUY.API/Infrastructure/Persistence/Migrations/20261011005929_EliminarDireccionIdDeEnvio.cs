using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EliminarDireccionIdDeEnvio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Envios_Direcciones_DireccionId",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_DireccionId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "DireccionId",
                table: "Envios");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DireccionId",
                table: "Envios",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_DireccionId",
                table: "Envios",
                column: "DireccionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_Direcciones_DireccionId",
                table: "Envios",
                column: "DireccionId",
                principalTable: "Direcciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
