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

        builder.HasOne(o => o.IntentoEntrega)
            .WithMany()
            .HasForeignKey(o => o.IntentoEntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.ReglaEntregaParcial)
            .WithMany()
            .HasForeignKey(o => o.ReglaEntregaParcialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(o => o.UsuarioId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(o => o.Huella)
            .HasMaxLength(64)
            .IsFixedLength();

        builder.Property(o => o.Mensaje)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(o => new
        {
            o.OperadorId,
            o.IdOperacion
        }).IsUnique();

        builder.HasIndex(o => o.RutaId);
    }
}
