using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Integraciones.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ClaveApiConfiguration : IEntityTypeConfiguration<ClaveApi>
{
    public void Configure(EntityTypeBuilder<ClaveApi> builder)
    {
        builder.ToTable("ClavesApi");

        builder.HasKey(c => c.Id);

        builder.HasOne<Operador>()
            .WithMany()
            .HasForeignKey(c => c.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Comercio>()
            .WithMany()
            .HasForeignKey(c => c.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.Nombre)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Prefijo)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Hash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(c => c.CreadaPorId)
            .HasMaxLength(450)
            .IsRequired();

        // Cada pedido con clave la busca por el hash
        builder.HasIndex(c => c.Hash)
            .IsUnique();

        builder.HasIndex(c => new { c.OperadorId, c.ComercioId });
    }
}
