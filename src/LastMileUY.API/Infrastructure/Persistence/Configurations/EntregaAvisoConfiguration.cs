using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Notificaciones.Domain;
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

        builder.HasOne<SuscripcionAvisos>()
            .WithMany(s => s.EntregasAvisos)
            .HasForeignKey(e => e.SuscripcionAvisosId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Envio>()
            .WithMany(e => e.EntregasAvisos)
            .HasForeignKey(e => e.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.EventoId)
            .IsUnique();

        builder.Property(e => e.TipoEvento)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Contenido)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.UltimoError)
            .HasMaxLength(1000);

        builder.HasIndex(e => new
        {
            e.Estado,
            e.FechaProximoIntento
        });
    }
}