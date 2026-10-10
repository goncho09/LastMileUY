using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class IncidenciaConfiguration : IEntityTypeConfiguration<Incidencia>
{
    public void Configure(EntityTypeBuilder<Incidencia> builder)
    {
        builder.ToTable("Incidencias");

        builder.HasKey(i => i.Id);

        // Cada incidencia pertenece a una ruta.
        builder.HasOne<Ruta>()
            .WithMany(r => r.Incidencias)
            .HasForeignKey(i => i.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Tipo)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(i => i.Descripcion)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(i => i.ObservacionesResolucion)
            .HasMaxLength(1000);

        // Facilita consultar las incidencias de una ruta
        // ordenadas por fecha.
        builder.HasIndex(i => new
        {
            i.RutaId,
            i.FechaHora
        });
    }
}