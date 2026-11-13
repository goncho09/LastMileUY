using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> builder)
    {
        builder.ToTable("Vehiculos");

        builder.HasKey(v => v.Id);

        builder.HasOne<Operador>()
            .WithMany(o => o.Vehiculos)
            .HasForeignKey(v => v.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(v => v.Matricula)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.Tipo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(v => v.CapacidadPesoKg)
            .HasPrecision(10, 2);

        builder.Property(v => v.CapacidadVolumenM3)
            .HasPrecision(10, 3);

        builder.HasIndex(v => new
        {
            v.OperadorId,
            v.Matricula
        }).IsUnique();
    }
}