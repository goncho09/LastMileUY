using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
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

        // Cada detalle pertenece a una liquidación.
        builder.HasOne<Liquidacion>()
            .WithMany(l => l.Detalles)
            .HasForeignKey(d => d.LiquidacionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada detalle corresponde a un envío.
        builder.HasOne<Envio>()
            .WithMany(e => e.DetallesLiquidacion)
            .HasForeignKey(d => d.EnvioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Importe cobrado por el envío.
        builder.Property(d => d.Importe)
            .HasPrecision(12, 2);

        // Un envío no puede repetirse dentro de una misma liquidación.
        builder.HasIndex(d => new
        {
            d.LiquidacionId,
            d.EnvioId
        }).IsUnique();
    }
}