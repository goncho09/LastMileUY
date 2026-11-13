using LastMileUY.API.Modules.Destinatarios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class DireccionConfiguration
    : IEntityTypeConfiguration<Direccion>
{
    public void Configure(EntityTypeBuilder<Direccion> builder)
    {
        builder.ToTable("Direcciones", t => t.HasCheckConstraint(
            "CK_Direcciones_Ubicacion",
            "(\"Latitud\" IS NULL AND \"Longitud\" IS NULL) OR (\"Latitud\" IS NOT NULL AND \"Longitud\" IS NOT NULL AND \"Latitud\" BETWEEN -90 AND 90 AND \"Longitud\" BETWEEN -180 AND 180)"));

        builder.HasKey(d => d.Id);

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

        builder.Property(d => d.Latitud)
            .HasPrecision(9, 6);

        builder.Property(d => d.Longitud)
            .HasPrecision(9, 6);

        builder.HasIndex(d => d.DestinatarioId);
    }
}