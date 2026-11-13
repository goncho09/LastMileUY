using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class DevolucionConfiguration
    : IEntityTypeConfiguration<Devolucion>
{
    public void Configure(EntityTypeBuilder<Devolucion> builder)
    {
        builder.ToTable("Devoluciones");

        builder.HasKey(d => d.Id);

        builder.HasOne<Envio>()
            .WithOne(e => e.Devolucion)
            .HasForeignKey<Devolucion>(d => d.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(d => d.Motivo)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.Observaciones)
            .HasMaxLength(1000);

        builder.HasIndex(d => d.EnvioId)
            .IsUnique();
    }
}