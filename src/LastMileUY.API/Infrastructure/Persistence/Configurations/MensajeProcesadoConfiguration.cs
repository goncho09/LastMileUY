using LastMileUY.API.Infrastructure.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class MensajeProcesadoConfiguration : IEntityTypeConfiguration<MensajeProcesado>
{
    public void Configure(EntityTypeBuilder<MensajeProcesado> builder)
    {
        builder.ToTable("MensajesProcesados");

        builder.HasKey(m => new { m.MensajeId, m.Consumidor });

        builder.Property(m => m.Consumidor).HasMaxLength(100);
    }
}
