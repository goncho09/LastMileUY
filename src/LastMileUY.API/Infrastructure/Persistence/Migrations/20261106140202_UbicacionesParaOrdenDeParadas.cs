using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UbicacionesParaOrdenDeParadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DepositoLatitud",
                table: "Operadores",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositoLongitud",
                table: "Operadores",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DireccionEntrega_Latitud",
                table: "Envios",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DireccionEntrega_Longitud",
                table: "Envios",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitud",
                table: "Direcciones",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitud",
                table: "Direcciones",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Operadores_Deposito",
                table: "Operadores",
                sql: "(\"DepositoLatitud\" IS NULL AND \"DepositoLongitud\" IS NULL) OR (\"DepositoLatitud\" IS NOT NULL AND \"DepositoLongitud\" IS NOT NULL AND \"DepositoLatitud\" BETWEEN -90 AND 90 AND \"DepositoLongitud\" BETWEEN -180 AND 180)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Envios_Ubicacion",
                table: "Envios",
                sql: "(\"DireccionEntrega_Latitud\" IS NULL AND \"DireccionEntrega_Longitud\" IS NULL) OR (\"DireccionEntrega_Latitud\" IS NOT NULL AND \"DireccionEntrega_Longitud\" IS NOT NULL AND \"DireccionEntrega_Latitud\" BETWEEN -90 AND 90 AND \"DireccionEntrega_Longitud\" BETWEEN -180 AND 180)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Direcciones_Ubicacion",
                table: "Direcciones",
                sql: "(\"Latitud\" IS NULL AND \"Longitud\" IS NULL) OR (\"Latitud\" IS NOT NULL AND \"Longitud\" IS NOT NULL AND \"Latitud\" BETWEEN -90 AND 90 AND \"Longitud\" BETWEEN -180 AND 180)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Operadores_Deposito",
                table: "Operadores");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Envios_Ubicacion",
                table: "Envios");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Direcciones_Ubicacion",
                table: "Direcciones");

            migrationBuilder.DropColumn(
                name: "DepositoLatitud",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "DepositoLongitud",
                table: "Operadores");

            migrationBuilder.DropColumn(
                name: "DireccionEntrega_Latitud",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "DireccionEntrega_Longitud",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "Direcciones");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "Direcciones");
        }
    }
}
