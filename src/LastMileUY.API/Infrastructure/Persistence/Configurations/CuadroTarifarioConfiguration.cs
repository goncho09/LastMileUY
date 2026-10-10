using LastMileUY.API.Modules.Tarifas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class CuadroTarifarioConfiguration
    : IEntityTypeConfiguration<CuadroTarifario>
{
    public void Configure(EntityTypeBuilder<CuadroTarifario> builder)
    {
        builder.ToTable("CuadrosTarifarios");

        builder.HasKey(c => c.Id);

        // Cada cuadro tarifario pertenece a un operador.
        builder.HasOne<Operador>()
            .WithMany(o => o.CuadrosTarifarios)
            .HasForeignKey(c => c.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un operador no puede repetir una versión de su cuadro tarifario.
        builder.HasIndex(c => new
        {
            c.OperadorId,
            c.Version
        }).IsUnique();

        // Un cuadro tarifario puede estar asociado a varios envíos.
        builder.HasMany(c => c.Envios)
            .WithOne()
            .HasForeignKey(e => e.CuadroTarifarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}