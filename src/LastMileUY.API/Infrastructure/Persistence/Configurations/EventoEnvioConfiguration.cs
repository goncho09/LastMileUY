using LastMileUY.API.Modules.Seguimiento.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class EventoEnvioConfiguration
    : IEntityTypeConfiguration<EventoEnvio>
{
    public void Configure(EntityTypeBuilder<EventoEnvio> builder)
    {
        builder.ToTable("EventosEnvio");

        builder.HasKey(e => e.Id);

        // Cada evento pertenece a un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.Eventos)
            .HasForeignKey(e => e.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.Tipo)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Descripcion)
            .HasMaxLength(1000);

        // Indica desde dónde se generó el evento.
        builder.Property(e => e.Origen)
            .HasMaxLength(100)
            .IsRequired();

        // Identificador del usuario o sistema responsable.
        builder.Property(e => e.ResponsableId)
            .HasMaxLength(450);

        // Ubicación del evento, cuando corresponda.
        builder.Property(e => e.Latitud)
            .HasPrecision(10, 7);

        builder.Property(e => e.Longitud)
            .HasPrecision(10, 7);

        // Facilita consultar el historial cronológico de un envío.
        builder.HasIndex(e => new
        {
            e.EnvioId,
            e.FechaHora
        });
    }
}