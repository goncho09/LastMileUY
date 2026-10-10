using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ParadaConfiguration : IEntityTypeConfiguration<Parada>
{
    public void Configure(EntityTypeBuilder<Parada> builder)
    {
        builder.ToTable("Paradas");

        builder.HasKey(p => p.Id);

        // Cada parada pertenece a una ruta.
        builder.HasOne<Ruta>()
            .WithMany(r => r.Paradas)
            .HasForeignKey(p => p.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada parada corresponde a un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.Paradas)
            .HasForeignKey(p => p.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        // No puede haber dos paradas con el mismo orden dentro de una ruta.
        builder.HasIndex(p => new
        {
            p.RutaId,
            p.Orden
        }).IsUnique();
    }
}