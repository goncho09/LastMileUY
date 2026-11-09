using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LastMileUY.API.Infrastructure.Persistence.Migrations
{
    // Un repartidor y un vehículo, una sola ruta abierta por día: la base lo asegura aunque dos
    // despachadores armen rutas a la vez (PlanificarRutaService traduce el choque a un 409).
    // Si una base ya tuviera dos rutas abiertas del mismo repartidor o vehículo el mismo día, la
    // migración falla: hay que cancelar una a mano antes de aplicarla.
    /// <inheritdoc />
    public partial class UnaRutaAbiertaPorDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Rutas_Fecha_RepartidorId_Abiertas",
                table: "Rutas",
                columns: new[] { "Fecha", "RepartidorId" },
                unique: true,
                filter: "\"Estado\" IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_Rutas_Fecha_VehiculoId_Abiertas",
                table: "Rutas",
                columns: new[] { "Fecha", "VehiculoId" },
                unique: true,
                filter: "\"Estado\" IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Rutas_Fecha_RepartidorId_Abiertas",
                table: "Rutas");

            migrationBuilder.DropIndex(
                name: "IX_Rutas_Fecha_VehiculoId_Abiertas",
                table: "Rutas");
        }
    }
}
