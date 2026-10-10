using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class NotificacionConfiguration
    : IEntityTypeConfiguration<Notificacion>
{
    public void Configure(EntityTypeBuilder<Notificacion> builder)
    {
        builder.ToTable("Notificaciones");

        builder.HasKey(n => n.Id);

        // Cada notificación pertenece a un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.Notificaciones)
            .HasForeignKey(n => n.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(n => n.Canal)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(n => n.Destino)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(n => n.Mensaje)
            .IsRequired();

        builder.Property(n => n.Error)
            .HasMaxLength(1000);

        // Facilita consultar las notificaciones de un envío.
        builder.HasIndex(n => new
        {
            n.EnvioId,
            n.FechaCreacion
        });
    }
}