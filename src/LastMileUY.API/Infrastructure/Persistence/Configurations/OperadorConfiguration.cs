using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class OperadorConfiguration : IEntityTypeConfiguration<Operador>
{
    public void Configure(EntityTypeBuilder<Operador> builder)
    {
        builder.ToTable("Operadores", t => t.HasCheckConstraint(
            "CK_Operadores_Deposito",
            "(\"DepositoLatitud\" IS NULL AND \"DepositoLongitud\" IS NULL) OR (\"DepositoLatitud\" IS NOT NULL AND \"DepositoLongitud\" IS NOT NULL AND \"DepositoLatitud\" BETWEEN -90 AND 90 AND \"DepositoLongitud\" BETWEEN -180 AND 180)"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.HasIndex(o => o.Nombre);

        builder.HasIndex(o => o.EsAmbientePruebas)
            .IsUnique()
            .HasFilter("\"EsAmbientePruebas\"")
            .HasDatabaseName("IX_Operadores_UnoDePruebas");

        builder.Property(o => o.ColorPrimario)
            .HasMaxLength(7);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Operadores_ColorPrimario",
            "\"ColorPrimario\" IS NULL OR \"ColorPrimario\" ~ '^#[0-9A-Fa-f]{6}$'"));

        builder.Property(o => o.LogoUrl)
            .HasMaxLength(500);

        builder.Property(o => o.DepositoLatitud)
            .HasPrecision(9, 6);

        builder.Property(o => o.DepositoLongitud)
            .HasPrecision(9, 6);
    }
}