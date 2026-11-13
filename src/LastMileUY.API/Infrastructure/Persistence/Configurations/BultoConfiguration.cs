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

        builder.Ignore(b => b.CambiosSinPublicar);

        builder.HasOne<Envio>()
            .WithMany(e => e.Bultos)
            .HasForeignKey(b => b.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.Codigo)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(b => b.Descripcion)
            .HasMaxLength(500);

        builder.Property(b => b.Estado)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(EstadoBulto.Pendiente);

        builder.Property(b => b.PesoKg)
            .HasPrecision(10, 2);

        builder.Property(b => b.LargoCm)
            .HasPrecision(10, 2);

        builder.Property(b => b.AnchoCm)
            .HasPrecision(10, 2);

        builder.Property(b => b.AltoCm)
            .HasPrecision(10, 2);

        builder.HasIndex(b => new
        {
            b.EnvioId,
            b.Codigo
        }).IsUnique();
    }
}