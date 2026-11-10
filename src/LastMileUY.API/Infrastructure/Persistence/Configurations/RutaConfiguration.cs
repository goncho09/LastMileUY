using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RutaConfiguration : IEntityTypeConfiguration<Ruta>
{
    public const string IndiceRepartidorPorDia = "IX_Rutas_Fecha_RepartidorId_Abiertas";
    public const string IndiceVehiculoPorDia = "IX_Rutas_Fecha_VehiculoId_Abiertas";

    // Abiertas = Planificada, Despachada o EnCurso (Ruta.EstadosAbiertos)
    private static readonly string FiltroRutasAbiertas =
        $"\"Estado\" IN ({string.Join(", ", Ruta.EstadosAbiertos.Select(e => (int)e))})";

    public void Configure(EntityTypeBuilder<Ruta> builder)
    {
        builder.ToTable("Rutas");

        builder.HasKey(r => r.Id);

        // Cada ruta pertenece a un operador logístico.
        builder.HasOne<Operador>()
            .WithMany(o => o.Rutas)
            .HasForeignKey(r => r.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada ruta tiene un repartidor asignado.
        builder.HasOne<Repartidor>()
            .WithMany(r => r.Rutas)
            .HasForeignKey(r => r.RepartidorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada ruta utiliza un vehículo.
        builder.HasOne<Vehiculo>()
            .WithMany(v => v.Rutas)
            .HasForeignKey(r => r.VehiculoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        // Un repartidor y un vehículo hacen una sola ruta abierta por día. PlanificarRutaService lo
        // controla antes de guardar, pero dos rutas armadas a la vez pasarían las dos ese control: la
        // base es la que lo asegura, y el servicio traduce el choque a un 409. La fecha va primero: así
        // quedan los índices de repartidor y vehículo solos, que sirven para todas las rutas (no solo las abiertas)
        builder.HasIndex(r => new { r.Fecha, r.RepartidorId })
            .IsUnique()
            .HasFilter(FiltroRutasAbiertas)
            .HasDatabaseName(IndiceRepartidorPorDia);

        builder.HasIndex(r => new { r.Fecha, r.VehiculoId })
            .IsUnique()
            .HasFilter(FiltroRutasAbiertas)
            .HasDatabaseName(IndiceVehiculoPorDia);

        // La versión de las reglas de entrega parcial con la que salió (las versiones no se borran)
        builder.HasOne(r => r.ReglaEntregaParcial)
            .WithMany()
            .HasForeignKey(r => r.ReglaEntregaParcialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
