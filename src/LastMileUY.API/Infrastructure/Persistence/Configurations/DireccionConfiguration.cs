using LastMileUY.API.Modules.Destinatarios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class DireccionConfiguration
    : IEntityTypeConfiguration<Direccion>
{
    public void Configure(EntityTypeBuilder<Direccion> builder)
    {
        builder.ToTable("Direcciones");

        builder.HasKey(d => d.Id);

        // Cada dirección pertenece a un destinatario.
        builder.HasOne<Destinatario>()
            .WithMany(d => d.Direcciones)
            .HasForeignKey(d => d.DestinatarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(d => d.Calle)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(d => d.Numero)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.Apartamento)
            .HasMaxLength(50);

        builder.Property(d => d.Ciudad)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Departamento)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.CodigoPostal)
            .HasMaxLength(20);

        builder.Property(d => d.Referencia)
            .HasMaxLength(300);

        // Facilita consultar las direcciones de un destinatario.
        builder.HasIndex(d => d.DestinatarioId);
    }
}