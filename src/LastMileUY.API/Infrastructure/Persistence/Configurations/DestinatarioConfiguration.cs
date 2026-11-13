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

        builder.HasIndex(d => d.Email);
    }
}