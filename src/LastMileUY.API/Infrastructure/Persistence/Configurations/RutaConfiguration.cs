using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RutaConfiguration : IEntityTypeConfiguration<Ruta>
{
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
    }
}