using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> builder)
    {
        builder.ToTable("Vehiculos");

        builder.HasKey(v => v.Id);

        // Cada vehículo pertenece a un operador logístico.
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

        // Precisión para las capacidades del vehículo.
        builder.Property(v => v.CapacidadPesoKg)
            .HasPrecision(10, 2);

        builder.Property(v => v.CapacidadVolumenM3)
            .HasPrecision(10, 3);

        // Un operador no puede registrar dos vehículos
        // con la misma matrícula.
        builder.HasIndex(v => new
        {
            v.OperadorId,
            v.Matricula
        }).IsUnique();
    }
}