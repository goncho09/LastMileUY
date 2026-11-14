using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class EnvioConfiguration : IEntityTypeConfiguration<Envio>
{
    public void Configure(EntityTypeBuilder<Envio> builder)
    {
        builder.ToTable("Envios", t => t.HasCheckConstraint(
            "CK_Envios_Ubicacion",
            "(\"DireccionEntrega_Latitud\" IS NULL AND \"DireccionEntrega_Longitud\" IS NULL) OR (\"DireccionEntrega_Latitud\" IS NOT NULL AND \"DireccionEntrega_Longitud\" IS NOT NULL AND \"DireccionEntrega_Latitud\" BETWEEN -90 AND 90 AND \"DireccionEntrega_Longitud\" BETWEEN -180 AND 180)"));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.CodigoSeguimiento)
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(e => e.CodigoSeguimiento)
            .IsUnique();

        builder.Property(e => e.ReferenciaExterna)
            .HasMaxLength(100);

        builder.HasIndex(e => new
        {
            e.OperadorId,
            e.ComercioId,
            e.ReferenciaExterna
        })
            .IsUnique()
            .HasFilter("\"ReferenciaExterna\" IS NOT NULL");

        builder.Property(e => e.Version)
            .IsRowVersion();

        builder.ComplexProperty(e => e.DireccionEntrega, direccion =>
        {
            direccion.IsRequired();

            direccion.Property(d => d.Calle)
                .HasMaxLength(150);

            direccion.Property(d => d.Numero)
                .HasMaxLength(20);

            direccion.Property(d => d.Apartamento)
                .HasMaxLength(50);

            direccion.Property(d => d.Ciudad)
                .HasMaxLength(100);

            direccion.Property(d => d.Departamento)
                .HasMaxLength(100);

            direccion.Property(d => d.CodigoPostal)
                .HasMaxLength(20);

            direccion.Property(d => d.Referencia)
                .HasMaxLength(300);

            direccion.Property(d => d.Latitud)
                .HasPrecision(9, 6);

            direccion.Property(d => d.Longitud)
                .HasPrecision(9, 6);
        });

        builder.HasOne<Operador>()
            .WithMany(o => o.Envios)
            .HasForeignKey(e => e.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Comercio>()
            .WithMany(c => c.Envios)
            .HasForeignKey(e => e.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Zona>()
            .WithMany()
            .HasForeignKey(e => e.ZonaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ReglaOperativa>()
            .WithMany()
            .HasForeignKey(e => e.ReglaOperativaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Destinatario>()
            .WithMany(d => d.Envios)
            .HasForeignKey(e => e.DestinatarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}