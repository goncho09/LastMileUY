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

        // Cada intento pertenece a un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.IntentosEntrega)
            .HasForeignKey(i => i.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        // El intento puede estar asociado a una parada.
        builder.HasOne<Parada>()
            .WithMany()
            .HasForeignKey(i => i.ParadaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Si la entrega falla, puede registrar un motivo.
        builder.HasOne<MotivoNoEntrega>()
            .WithMany(m => m.IntentosEntrega)
            .HasForeignKey(i => i.MotivoNoEntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Observaciones)
            .HasMaxLength(1000);

        // Un envío no puede repetir el número de intento.
        builder.HasIndex(i => new
        {
            i.EnvioId,
            i.NumeroIntento
        }).IsUnique();
    }
}