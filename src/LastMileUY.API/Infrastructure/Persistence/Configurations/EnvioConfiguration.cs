using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class EnvioConfiguration : IEntityTypeConfiguration<Envio>
{
    public void Configure(EntityTypeBuilder<Envio> builder)
    {
        builder.ToTable("Envios");

        builder.HasKey(e => e.Id);

        builder.HasIndex(e => e.CodigoSeguimiento)
            .IsUnique();

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
        });
        
        // Relaciones del envío con las entidades principales.
        builder.HasOne<Operador>()
            .WithMany(o => o.Envios)
            .HasForeignKey(e => e.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Comercio>()
            .WithMany(c => c.Envios)
            .HasForeignKey(e => e.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Destinatario>()
            .WithMany(d => d.Envios)
            .HasForeignKey(e => e.DestinatarioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Direccion>()
            .WithMany(d => d.Envios)
            .HasForeignKey(e => e.DireccionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}