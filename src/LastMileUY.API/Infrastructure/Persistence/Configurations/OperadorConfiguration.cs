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

        // Las relaciones con las demás entidades
        // se configuran en sus respectivas clases.
    }
}