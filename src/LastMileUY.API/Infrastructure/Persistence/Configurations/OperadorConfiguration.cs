using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class OperadorConfiguration : IEntityTypeConfiguration<Operador>
{
    public void Configure(EntityTypeBuilder<Operador> builder)
    {
        builder.ToTable("Operadores");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        // Facilita buscar operadores por nombre.
        builder.HasIndex(o => o.Nombre);

        // El color se escribe tal cual en el CSS del Backoffice: la base solo acepta #RRGGBB
        builder.Property(o => o.ColorPrimario)
            .HasMaxLength(7);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Operadores_ColorPrimario",
            "\"ColorPrimario\" IS NULL OR \"ColorPrimario\" ~ '^#[0-9A-Fa-f]{6}$'"));

        builder.Property(o => o.LogoUrl)
            .HasMaxLength(500);

        // Las relaciones con las demás entidades
        // se configuran en sus respectivas clases.
    }
}