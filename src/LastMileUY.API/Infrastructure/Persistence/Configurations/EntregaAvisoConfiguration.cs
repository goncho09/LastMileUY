using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class EntregaAvisoConfiguration
    : IEntityTypeConfiguration<EntregaAviso>
{
    public void Configure(EntityTypeBuilder<EntregaAviso> builder)
    {
        builder.ToTable("EntregasAvisos");

        builder.HasKey(e => e.Id);

        // Cada entrega corresponde a una suscripción de webhook.
        builder.HasOne<SuscripcionAvisos>()
            .WithMany(s => s.EntregasAvisos)
            .HasForeignKey(e => e.SuscripcionAvisosId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada webhook está relacionado con un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.EntregasAvisos)
            .HasForeignKey(e => e.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Identificador único del evento para evitar duplicados.
        builder.HasIndex(e => e.EventoId)
            .IsUnique();

        builder.Property(e => e.TipoEvento)
            .HasMaxLength(100)
            .IsRequired();

        // Contenido JSON que se enviará al comercio.
        builder.Property(e => e.Contenido)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.UltimoError)
            .HasMaxLength(1000);

        // Facilita encontrar webhooks pendientes de reintento.
        builder.HasIndex(e => new
        {
            e.Estado,
            e.FechaProximoIntento
        });
    }
}