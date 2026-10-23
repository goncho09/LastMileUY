using LastMileUY.API.Infrastructure.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class MensajeOutboxConfiguration : IEntityTypeConfiguration<MensajeOutbox>
{
    public void Configure(EntityTypeBuilder<MensajeOutbox> builder)
    {
        builder.ToTable("MensajesOutbox");

        builder.HasKey(m => m.Id);

        // El id lo pone la app: así el INSERT no le pide nada de vuelta a la base. La aplicación
        // solo puede insertar en esta tabla, no leerla (ver la migración Outbox).
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.HasOne(m => m.Envio)
            .WithMany()
            .HasForeignKey(m => m.EnvioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(m => m.Tipo).HasMaxLength(50).IsRequired();
        // Mismos largos que en Envios y EventosEnvio, de donde salen
        builder.Property(m => m.CodigoSeguimiento).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Estado).HasMaxLength(100).IsRequired();
        builder.Property(m => m.EstadoAnterior).HasMaxLength(100);
        builder.Property(m => m.Origen).HasMaxLength(100).IsRequired();
        builder.Property(m => m.CorrelacionId).HasMaxLength(64);
        builder.Property(m => m.UltimoError).HasMaxLength(1000);

        // Lo que busca el publicador: los que faltan publicar, en orden
        builder.HasIndex(m => m.FechaCreacion)
            .HasFilter("\"FechaPublicacion\" IS NULL");
    }
}
