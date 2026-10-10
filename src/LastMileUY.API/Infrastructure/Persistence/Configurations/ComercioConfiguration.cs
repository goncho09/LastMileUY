using LastMileUY.API.Modules.Comercios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ComercioConfiguration : IEntityTypeConfiguration<Comercio>
{
    public void Configure(EntityTypeBuilder<Comercio> builder)
    {
        builder.ToTable("Comercios");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(c => c.RazonSocial)
            .HasMaxLength(200);

        builder.Property(c => c.RUT)
            .HasMaxLength(20);

        builder.Property(c => c.Email)
            .HasMaxLength(250);

        builder.Property(c => c.Telefono)
            .HasMaxLength(30);

        // Evita registrar dos comercios con el mismo RUT.
        builder.HasIndex(c => c.RUT)
            .IsUnique();
    }
}