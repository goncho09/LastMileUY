using LastMileUY.API.Modules.Tarifas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class AjusteTarifaConfiguration
    : IEntityTypeConfiguration<AjusteTarifa>
{
    public void Configure(EntityTypeBuilder<AjusteTarifa> builder)
    {
        builder.ToTable("AjustesTarifa");

        builder.HasKey(a => a.Id);

        builder.HasOne<Tarifa>()
            .WithMany(t => t.Ajustes)
            .HasForeignKey(a => a.TarifaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.Porcentaje)
            .HasPrecision(7, 2);

        builder.Property(a => a.Nombre)
            .HasMaxLength(150)
            .IsRequired();
    }
}