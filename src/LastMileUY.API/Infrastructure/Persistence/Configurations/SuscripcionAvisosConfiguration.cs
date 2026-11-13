using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class SuscripcionAvisosConfiguration
    : IEntityTypeConfiguration<SuscripcionAvisos>
{
    public void Configure(EntityTypeBuilder<SuscripcionAvisos> builder)
    {
        builder.ToTable("SuscripcionesAvisos");

        builder.HasKey(s => s.Id);

        builder.HasOne<Operador>()
            .WithMany(o => o.SuscripcionesAvisos)
            .HasForeignKey(s => s.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Comercio>()
            .WithMany(c => c.SuscripcionesAvisos)
            .HasForeignKey(s => s.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(s => s.TipoEvento)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.UrlDestino)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(s => s.ClaveFirma)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(s => s.VersionContrato)
            .HasDefaultValue(1);

        builder.HasIndex(s => new
        {
            s.OperadorId,
            s.ComercioId,
            s.TipoEvento,
            s.Activa
        });
    }
}