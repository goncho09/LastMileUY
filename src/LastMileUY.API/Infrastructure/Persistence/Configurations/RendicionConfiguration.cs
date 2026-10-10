using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RendicionConfiguration : IEntityTypeConfiguration<Rendicion>
{
    public void Configure(EntityTypeBuilder<Rendicion> builder)
    {
        builder.ToTable("Rendiciones");

        builder.HasKey(r => r.Id);

        // Cada rendición corresponde a una única ruta.
        builder.HasOne<Ruta>()
            .WithOne(r => r.Rendicion)
            .HasForeignKey<Rendicion>(r => r.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Observaciones)
            .HasMaxLength(1000);

        // Una ruta solo puede tener una rendición.
        builder.HasIndex(r => r.RutaId)
            .IsUnique();
    }
}