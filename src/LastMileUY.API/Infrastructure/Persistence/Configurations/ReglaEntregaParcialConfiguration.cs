using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class ReglaEntregaParcialConfiguration : IEntityTypeConfiguration<ReglaEntregaParcial>
{
    public void Configure(EntityTypeBuilder<ReglaEntregaParcial> builder)
    {
        builder.ToTable("ReglasEntregaParcial");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RecargoNuevaVisita)
            .HasColumnType("numeric(10,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.HasOne<Operador>()
            .WithMany()
            .HasForeignKey(r => r.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Motivo)
            .HasMaxLength(ReglaEntregaParcial.LargoMaximoMotivo);

        builder.Property(r => r.PublicadaPorId)
            .HasMaxLength(450)
            .IsRequired();

        // Una versión por número: si dos publican a la vez, la segunda choca y se reintenta
        builder.HasIndex(r => new { r.OperadorId, r.Version })
            .IsUnique();
    }
}
