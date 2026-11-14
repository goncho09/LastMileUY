using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ZonaEnEnviosYCoberturaDeZonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Departamento",
                table: "Zonas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Localidades",
                table: "Zonas",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<int>(
                name: "ZonaId",
                table: "Envios",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_ZonaId",
                table: "Envios",
                column: "ZonaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Envios_Zonas_ZonaId",
                table: "Envios",
                column: "ZonaId",
                principalTable: "Zonas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Envios_Zonas_ZonaId",
                table: "Envios");

            migrationBuilder.DropIndex(
                name: "IX_Envios_ZonaId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "Departamento",
                table: "Zonas");

            migrationBuilder.DropColumn(
                name: "Localidades",
                table: "Zonas");

            migrationBuilder.DropColumn(
                name: "ZonaId",
                table: "Envios");
        }
    }
}
