using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Identidad.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RepartidorConfiguration : IEntityTypeConfiguration<Repartidor>
{
    public void Configure(EntityTypeBuilder<Repartidor> builder)
    {
        builder.ToTable("Repartidores");

        builder.HasKey(r => r.Id);

        // Cada repartidor pertenece a un operador logístico.
        builder.HasOne<Operador>()
            .WithMany(o => o.Repartidores)
            .HasForeignKey(r => r.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cada repartidor está vinculado a un usuario de Identity.
        builder.HasOne<Usuario>()
            .WithOne()
            .HasForeignKey<Repartidor>(r => r.UsuarioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un usuario solo puede tener un registro de repartidor.
        builder.HasIndex(r => r.UsuarioId)
            .IsUnique();

        builder.Property(r => r.LicenciaConducir)
            .HasMaxLength(50);
    }
}