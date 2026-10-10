using LastMileUY.API.Modules.Identidad.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Comercios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        // Conservamos la tabla de ASP.NET Identity.
        builder.ToTable("AspNetUsers");

        builder.Property(u => u.Nombre)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(u => u.Apellido)
            .HasMaxLength(100)
            .IsRequired();

        // Un usuario puede pertenecer a un operador.
        builder.HasOne<Operador>()
            .WithMany(o => o.Usuarios)
            .HasForeignKey(u => u.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un usuario puede pertenecer a un comercio.
        builder.HasOne<Comercio>()
            .WithMany(c => c.Usuarios)
            .HasForeignKey(u => u.ComercioId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un usuario no puede pertenecer simultáneamente
        // a un operador y a un comercio.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Usuarios_Ambito",
            "\"OperadorId\" IS NULL OR \"ComercioId\" IS NULL"
        ));
    }
}