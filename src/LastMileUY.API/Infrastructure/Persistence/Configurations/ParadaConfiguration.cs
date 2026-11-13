using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ParadaConfiguration : IEntityTypeConfiguration<Parada>
{
    public void Configure(EntityTypeBuilder<Parada> builder)
    {
        builder.ToTable("Paradas");

        builder.HasKey(p => p.Id);

        builder.HasOne<Ruta>()
            .WithMany(r => r.Paradas)
            .HasForeignKey(p => p.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Envio>()
            .WithMany(e => e.Paradas)
            .HasForeignKey(p => p.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new
        {
            p.RutaId,
            p.Orden
        }).IsUnique();

        builder.HasMany(p => p.Bultos)
            .WithOne()
            .HasForeignKey(b => b.ParadaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BultoParadaConfiguration : IEntityTypeConfiguration<BultoParada>
{
    public void Configure(EntityTypeBuilder<BultoParada> builder)
    {
        builder.ToTable("BultosParada");

        builder.HasKey(b => new { b.ParadaId, b.BultoId });

        builder.HasOne<Bulto>()
            .WithMany()
            .HasForeignKey(b => b.BultoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}