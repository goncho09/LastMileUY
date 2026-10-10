using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
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

        // Cada suscripción pertenece a un operador.
        builder.HasOne<Operador>()
            .WithMany(o => o.SuscripcionesAvisos)
            .HasForeignKey(s => s.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada suscripción pertenece a un comercio.
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

        // Facilita buscar las suscripciones activas
        // de un comercio para un evento determinado.
        builder.HasIndex(s => new
        {
            s.OperadorId,
            s.ComercioId,
            s.TipoEvento,
            s.Activa
        });
    }
}