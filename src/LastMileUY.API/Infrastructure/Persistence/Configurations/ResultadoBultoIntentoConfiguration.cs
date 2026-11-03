using LastMileUY.API.Modules.Entregas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ResultadoBultoIntentoConfiguration
    : IEntityTypeConfiguration<ResultadoBultoIntento>
{
    public void Configure(EntityTypeBuilder<ResultadoBultoIntento> builder)
    {
        builder.ToTable("ResultadosBultosIntentos");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.IntentoEntrega)
            .WithMany(i => i.ResultadosBultos)
            .HasForeignKey(x => x.IntentoEntregaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bulto)
            .WithMany()
            .HasForeignKey(x => x.BultoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.IntentoEntregaId,
            x.BultoId
        }).IsUnique();
    }
}