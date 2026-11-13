using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class FranjaHorariaConfiguration
    : IEntityTypeConfiguration<FranjaHoraria>
{
    public void Configure(EntityTypeBuilder<FranjaHoraria> builder)
    {
        builder.ToTable("FranjasHorarias");

        builder.HasKey(f => f.Id);

        builder.HasOne<Zona>()
            .WithMany(z => z.FranjasHorarias)
            .HasForeignKey(f => f.ZonaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(f => f.Nombre)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(f => new
        {
            f.ZonaId,
            f.Nombre
        }).IsUnique();
    }
}