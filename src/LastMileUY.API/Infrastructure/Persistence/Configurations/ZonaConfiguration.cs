using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ZonaConfiguration : IEntityTypeConfiguration<Zona>
{
    public void Configure(EntityTypeBuilder<Zona> builder)
    {
        builder.ToTable("Zonas");

        builder.HasKey(z => z.Id);

        // Cada zona pertenece a un operador logístico.
        builder.HasOne<Operador>()
            .WithMany(o => o.Zonas)
            .HasForeignKey(z => z.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(z => z.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(z => z.Descripcion)
            .HasMaxLength(500);

        // Un operador no puede tener dos zonas con el mismo nombre.
        builder.HasIndex(z => new
        {
            z.OperadorId,
            z.Nombre
        }).IsUnique();
    }
}