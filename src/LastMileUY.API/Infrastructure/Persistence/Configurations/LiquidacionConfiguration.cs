using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class LiquidacionConfiguration
    : IEntityTypeConfiguration<Liquidacion>
{
    public void Configure(EntityTypeBuilder<Liquidacion> builder)
    {
        builder.ToTable("Liquidaciones");

        builder.HasKey(l => l.Id);

        // Cada liquidación pertenece a un operador logístico.
        builder.HasOne<Operador>()
            .WithMany(o => o.Liquidaciones)
            .HasForeignKey(l => l.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada liquidación corresponde a un comercio.
        builder.HasOne<Comercio>()
            .WithMany(c => c.Liquidaciones)
            .HasForeignKey(l => l.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Importe total de la liquidación.
        builder.Property(l => l.ImporteTotal)
            .HasPrecision(12, 2);

        builder.Property(l => l.Observaciones)
            .HasMaxLength(1000);

        // Facilita consultar las liquidaciones de un comercio
        // por operador y período.
        builder.HasIndex(l => new
        {
            l.OperadorId,
            l.ComercioId,
            l.PeriodoDesde,
            l.PeriodoHasta
        });
    }
}