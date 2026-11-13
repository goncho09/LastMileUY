using LastMileUY.API.Modules.Mensajeria.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public sealed class CorreoSeguimientoConfiguration
    : IEntityTypeConfiguration<CorreoSeguimiento>
{
    public void Configure(
        EntityTypeBuilder<CorreoSeguimiento> builder)
    {
        builder.ToTable("CorreosSeguimiento");

        builder.HasKey(x => x.EventoId);

        builder.Property(x => x.Estado)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.UltimoError)
            .HasColumnType("text");

        builder.HasIndex(x => new
        {
            x.OperadorId,
            x.ComercioId,
            x.Estado
        });
    }
}