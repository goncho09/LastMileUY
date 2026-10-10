using LastMileUY.API.Modules.Entregas.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class PruebaEntregaConfiguration
    : IEntityTypeConfiguration<PruebaEntrega>
{
    public void Configure(EntityTypeBuilder<PruebaEntrega> builder)
    {
        builder.ToTable("PruebasEntrega");

        builder.HasKey(p => p.Id);

        // Cada prueba pertenece a un único intento de entrega.
        builder.HasOne<IntentoEntrega>()
            .WithOne(i => i.PruebaEntrega)
            .HasForeignKey<PruebaEntrega>(p => p.IntentoEntregaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Referencias a los archivos almacenados.
        builder.Property(p => p.FirmaArchivoKey)
            .HasMaxLength(500);

        builder.Property(p => p.FotoArchivoKey)
            .HasMaxLength(500);

        // Datos de la persona que recibió el envío.
        builder.Property(p => p.NombreReceptor)
            .HasMaxLength(150);

        builder.Property(p => p.DocumentoReceptor)
            .HasMaxLength(50);

        builder.Property(p => p.Observaciones)
            .HasMaxLength(1000);

        // Coordenadas donde se registró la evidencia.
        builder.Property(p => p.Latitud)
            .HasPrecision(10, 7);

        builder.Property(p => p.Longitud)
            .HasPrecision(10, 7);

        // Un intento solo puede tener una prueba de entrega.
        builder.HasIndex(p => p.IntentoEntregaId)
            .IsUnique();
    }
}