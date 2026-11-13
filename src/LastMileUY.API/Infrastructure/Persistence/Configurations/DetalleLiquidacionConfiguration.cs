using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Tarifas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class DetalleLiquidacionConfiguration
    : IEntityTypeConfiguration<DetalleLiquidacion>
{
    public void Configure(EntityTypeBuilder<DetalleLiquidacion> builder)
    {
        builder.ToTable("DetallesLiquidacion");

        builder.HasKey(d => d.Id);

        builder.HasOne<Liquidacion>()
            .WithMany(l => l.Detalles)
            .HasForeignKey(d => d.LiquidacionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Envio>()
            .WithMany(e => e.DetallesLiquidacion)
            .HasForeignKey(d => d.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(d => d.Importe)
            .HasPrecision(12, 2);

        builder.HasIndex(d => new
        {
            d.LiquidacionId,
            d.EnvioId
        }).IsUnique();
    }
}