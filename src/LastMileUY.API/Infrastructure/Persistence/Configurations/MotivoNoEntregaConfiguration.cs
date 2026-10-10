using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class MotivoNoEntregaConfiguration
    : IEntityTypeConfiguration<MotivoNoEntrega>
{
    public void Configure(EntityTypeBuilder<MotivoNoEntrega> builder)
    {
        builder.ToTable("MotivosNoEntrega");

        builder.HasKey(m => m.Id);

        // Cada motivo pertenece a un operador logístico.
        builder.HasOne<Operador>()
            .WithMany(o => o.MotivosNoEntrega)
            .HasForeignKey(m => m.OperadorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.Nombre)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(m => m.Descripcion)
            .HasMaxLength(500);

        // Un operador no puede registrar dos motivos con el mismo nombre.
        builder.HasIndex(m => new
        {
            m.OperadorId,
            m.Nombre
        }).IsUnique();
    }
}