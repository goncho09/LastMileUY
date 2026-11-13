using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class PosicionConfiguration : IEntityTypeConfiguration<Posicion>
{
    public void Configure(EntityTypeBuilder<Posicion> builder)
    {
        builder.ToTable("Posiciones");

        builder.HasKey(p => p.Id);

        builder.HasOne<Ruta>()
            .WithMany(r => r.Posiciones)
            .HasForeignKey(p => p.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(p => p.Latitud)
            .HasPrecision(10, 7);

        builder.Property(p => p.Longitud)
            .HasPrecision(10, 7);

        builder.HasIndex(p => new
        {
            p.RutaId,
            p.FechaHora
        });
    }
}