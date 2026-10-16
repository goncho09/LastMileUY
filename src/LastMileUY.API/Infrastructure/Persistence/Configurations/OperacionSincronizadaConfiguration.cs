using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Sincronizacion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class OperacionSincronizadaConfiguration : IEntityTypeConfiguration<OperacionSincronizada>
{
    public void Configure(EntityTypeBuilder<OperacionSincronizada> builder)
    {
        builder.ToTable("OperacionesSincronizadas");

        builder.HasKey(o => o.Id);

        builder.HasOne<Operador>()
            .WithMany()
            .HasForeignKey(o => o.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // El intento que generó la operación (si se aplicó)
        builder.HasOne(o => o.IntentoEntrega)
            .WithMany()
            .HasForeignKey(o => o.IntentoEntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.UsuarioId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(o => o.Mensaje)
            .HasMaxLength(500)
            .IsRequired();

        // Lo que evita aplicar dos veces la misma operación: un reenvío choca con este índice
        builder.HasIndex(o => new
        {
            o.OperadorId,
            o.IdOperacion
        }).IsUnique();

        // El historial del Backoffice se consulta por ruta
        builder.HasIndex(o => o.RutaId);
    }
}
