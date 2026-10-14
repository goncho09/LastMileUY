using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadVisualOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorPrimario",
                table: "Operadores",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Operadores",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Operadores_ColorPrimario",
                table: "Operadores",
                sql: "\"ColorPrimario\" IS NULL OR \"ColorPrimario\" ~ '^#[0-9A-Fa-f]{6}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Operadores_ColorPrimario",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "ColorPrimario",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Operadores");
        }
    }
}
