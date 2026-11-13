using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ReglaOperativaConfiguration
    : IEntityTypeConfiguration<ReglaOperativa>
{
    public void Configure(EntityTypeBuilder<ReglaOperativa> builder)
    {
        builder.ToTable("ReglasOperativas");

        builder.HasKey(r => r.Id);

        builder.HasOne<Operador>()
            .WithMany(o => o.ReglasOperativas)
            .HasForeignKey(r => r.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(r => r.Descripcion)
            .HasMaxLength(500);

        builder.HasIndex(r => new
        {
            r.OperadorId,
            r.Modalidad
        })
            .IsUnique()
            .HasFilter("\"Activa\" = true");
    }
}