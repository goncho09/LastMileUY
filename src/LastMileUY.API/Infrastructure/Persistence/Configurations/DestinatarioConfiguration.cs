using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class DestinatarioConfiguration
    : IEntityTypeConfiguration<Destinatario>
{
    public void Configure(EntityTypeBuilder<Destinatario> builder)
    {
        builder.ToTable("Destinatarios");

        builder.HasKey(d => d.Id);

        // Cada destinatario pertenece a un comercio.
        builder.HasOne<Comercio>()
            .WithMany()
            .HasForeignKey(d => d.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.ComercioId);

        builder.Property(d => d.Nombre)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Apellido)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Email)
            .HasMaxLength(250);

        builder.Property(d => d.Telefono)
            .HasMaxLength(30);

        // Facilita buscar destinatarios por correo electrónico.
        builder.HasIndex(d => d.Email);
    }
}