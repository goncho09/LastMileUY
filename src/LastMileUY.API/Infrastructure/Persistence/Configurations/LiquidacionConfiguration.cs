using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;
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

        builder.HasOne<Operador>()
            .WithMany(o => o.Liquidaciones)
            .HasForeignKey(l => l.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Comercio>()
            .WithMany(c => c.Liquidaciones)
            .HasForeignKey(l => l.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(l => l.ImporteTotal)
            .HasPrecision(12, 2);

        builder.Property(l => l.Observaciones)
            .HasMaxLength(1000);

        builder.HasIndex(l => new
        {
            l.OperadorId,
            l.ComercioId,
            l.PeriodoDesde,
            l.PeriodoHasta
        });
    }
}