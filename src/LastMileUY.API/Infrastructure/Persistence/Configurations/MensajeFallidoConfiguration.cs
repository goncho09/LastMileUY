using LastMileUY.API.Modules.Mensajeria.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class MensajeFallidoConfiguration
    : IEntityTypeConfiguration<MensajeFallido>
{
    public void Configure(EntityTypeBuilder<MensajeFallido> builder)
    {
        builder.ToTable("MensajesFallidos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MensajeId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.CorrelacionId)
            .HasMaxLength(200);

        builder.Property(x => x.Tipo)
            .HasMaxLength(200);

        builder.Property(x => x.Contenido)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.UltimoError)
            .HasColumnType("text");

        builder.Property(x => x.Estado)
            .HasConversion<string>()
            .HasMaxLength(40);

        builder.Property(x => x.SolicitadoPorId)
            .HasMaxLength(450);

        builder.HasIndex(x => x.MensajeId);

        builder.HasIndex(x => new
        {
            x.Estado,
            x.FechaFallo
        });
    }
}