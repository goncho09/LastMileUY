using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class FuncionalidadOperadorConfiguration : IEntityTypeConfiguration<FuncionalidadOperador>
{
    public void Configure(EntityTypeBuilder<FuncionalidadOperador> builder)
    {
        builder.ToTable("FuncionalidadesOperador");

        builder.HasKey(f => new { f.OperadorId, f.Clave });

        builder.HasOne<Operador>()
            .WithMany()
            .HasForeignKey(f => f.OperadorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(f => f.Clave)
            .HasMaxLength(50);

        builder.Property(f => f.ModificadaPor)
            .HasMaxLength(450);
    }
}
