using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class IntentoEntregaConfiguration
    : IEntityTypeConfiguration<IntentoEntrega>
{
    public void Configure(EntityTypeBuilder<IntentoEntrega> builder)
    {
        builder.ToTable("IntentosEntrega");

        builder.HasKey(i => i.Id);

        builder.HasOne<Envio>()
            .WithMany(e => e.IntentosEntrega)
            .HasForeignKey(i => i.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Parada>()
            .WithMany()
            .HasForeignKey(i => i.ParadaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MotivoNoEntrega>()
            .WithMany(m => m.IntentosEntrega)
            .HasForeignKey(i => i.MotivoNoEntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Observaciones)
            .HasMaxLength(1000);

        builder.HasIndex(i => new
        {
            i.EnvioId,
            i.NumeroIntento
        }).IsUnique();

        builder.Property(i => i.Parcial)
            .IsRequired()
            .HasDefaultValue(false);
    }
}