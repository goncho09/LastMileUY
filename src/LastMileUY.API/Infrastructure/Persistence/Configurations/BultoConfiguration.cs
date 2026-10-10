using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class BultoConfiguration : IEntityTypeConfiguration<Bulto>
{
    public void Configure(EntityTypeBuilder<Bulto> builder)
    {
        builder.ToTable("Bultos");

        builder.HasKey(b => b.Id);

        // Cada bulto pertenece a un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.Bultos)
            .HasForeignKey(b => b.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.Codigo)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(b => b.Descripcion)
            .HasMaxLength(500);

        // Peso en kilogramos.
        builder.Property(b => b.PesoKg)
            .HasPrecision(10, 2);

        // Dimensiones en centímetros.
        builder.Property(b => b.LargoCm)
            .HasPrecision(10, 2);

        builder.Property(b => b.AnchoCm)
            .HasPrecision(10, 2);

        builder.Property(b => b.AltoCm)
            .HasPrecision(10, 2);

        // No se puede repetir el código de un bulto
        // dentro del mismo envío.
        builder.HasIndex(b => new
        {
            b.EnvioId,
            b.Codigo
        }).IsUnique();
    }
}