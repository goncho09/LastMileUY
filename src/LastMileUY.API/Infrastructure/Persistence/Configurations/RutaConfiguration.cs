using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RutaConfiguration : IEntityTypeConfiguration<Ruta>
{
    public const string IndiceRepartidorPorDia = "IX_Rutas_Fecha_RepartidorId_Abiertas";
    public const string IndiceVehiculoPorDia = "IX_Rutas_Fecha_VehiculoId_Abiertas";

    private static readonly string FiltroRutasAbiertas =
        $"\"Estado\" IN ({string.Join(", ", Ruta.EstadosAbiertos.Select(e => (int)e))})";

    public void Configure(EntityTypeBuilder<Ruta> builder)
    {
        builder.ToTable("Rutas");

        builder.HasKey(r => r.Id);

        builder.HasOne<Operador>()
            .WithMany(o => o.Rutas)
            .HasForeignKey(r => r.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Repartidor>()
            .WithMany(r => r.Rutas)
            .HasForeignKey(r => r.RepartidorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Vehiculo>()
            .WithMany(v => v.Rutas)
            .HasForeignKey(r => r.VehiculoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.HasIndex(r => new { r.Fecha, r.RepartidorId })
            .IsUnique()
            .HasFilter(FiltroRutasAbiertas)
            .HasDatabaseName(IndiceRepartidorPorDia);

        builder.HasIndex(r => new { r.Fecha, r.VehiculoId })
            .IsUnique()
            .HasFilter(FiltroRutasAbiertas)
            .HasDatabaseName(IndiceVehiculoPorDia);

        builder.HasOne(r => r.ReglaEntregaParcial)
            .WithMany()
            .HasForeignKey(r => r.ReglaEntregaParcialId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
