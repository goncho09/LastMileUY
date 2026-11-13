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

        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.HasOne(m => m.Envio)
            .WithMany()
            .HasForeignKey(m => m.EnvioId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(m => m.Tipo).HasMaxLength(50).IsRequired();
        builder.Property(m => m.CodigoSeguimiento).HasMaxLength(20).IsRequired();
        builder.Property(m => m.Estado).HasMaxLength(100).IsRequired();
        builder.Property(m => m.EstadoAnterior).HasMaxLength(100);
        builder.Property(m => m.Origen).HasMaxLength(100).IsRequired();
        builder.Property(m => m.CorrelacionId).HasMaxLength(64);
        builder.Property(m => m.CodigoBulto).HasMaxLength(100);
        builder.Property(m => m.MotivoNoEntrega).HasMaxLength(150);
        builder.Property(m => m.UltimoError).HasMaxLength(1000);

        builder.HasIndex(m => m.FechaCreacion)
            .HasFilter("\"FechaPublicacion\" IS NULL");
    }
}
