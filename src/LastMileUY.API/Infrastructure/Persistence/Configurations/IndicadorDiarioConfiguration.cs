using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Reportes.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class IndicadorDiarioConfiguration : IEntityTypeConfiguration<IndicadorDiario>
{
    public void Configure(EntityTypeBuilder<IndicadorDiario> builder)
    {
        builder.ToTable("IndicadoresDiarios");

        builder.HasKey(i => i.Id);

        builder.HasOne<Operador>().WithMany().HasForeignKey(i => i.OperadorId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(i => i.Importe).HasPrecision(14, 2);

        builder.HasIndex(i => new { i.OperadorId, i.Dia }).IsUnique();
    }
}
