using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RendicionConfiguration : IEntityTypeConfiguration<Rendicion>
{
    public void Configure(EntityTypeBuilder<Rendicion> builder)
    {
        builder.ToTable("Rendiciones");

        builder.HasKey(r => r.Id);

        builder.HasOne<Ruta>()
            .WithOne(r => r.Rendicion)
            .HasForeignKey<Rendicion>(r => r.RutaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Observaciones)
            .HasMaxLength(1000);

        builder.Property(r => r.ObservacionesRepartidor).HasMaxLength(500);

        builder.Property(r => r.ConfirmadaPor).HasMaxLength(450);

        builder.Property(r => r.BultosDeclarados).HasColumnType("text[]").HasDefaultValueSql("'{}'");

        builder.Property(r => r.BultosRecibidos).HasColumnType("text[]").HasDefaultValueSql("'{}'");

        builder.Property(r => r.Faltantes).HasColumnType("text[]").HasDefaultValueSql("'{}'");

        builder.Property(r => r.Sobrantes).HasColumnType("text[]").HasDefaultValueSql("'{}'");

        builder.Ignore(r => r.Confirmada);

        builder.HasIndex(r => r.RutaId)
            .IsUnique();
    }
}