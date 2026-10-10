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

        // Cada posición GPS pertenece a una ruta.
        builder.HasOne<Ruta>()
            .WithMany(r => r.Posiciones)
            .HasForeignKey(p => p.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Precisión de las coordenadas geográficas.
        builder.Property(p => p.Latitud)
            .HasPrecision(10, 7);

        builder.Property(p => p.Longitud)
            .HasPrecision(10, 7);

        // Facilita consultar el recorrido de una ruta
        // ordenado por la fecha de cada posición.
        builder.HasIndex(p => new
        {
            p.RutaId,
            p.FechaHora
        });
    }
}