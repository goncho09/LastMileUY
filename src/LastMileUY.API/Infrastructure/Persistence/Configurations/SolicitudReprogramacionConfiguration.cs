using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Seguimiento.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class SolicitudReprogramacionConfiguration : IEntityTypeConfiguration<SolicitudReprogramacion>
{
    public void Configure(EntityTypeBuilder<SolicitudReprogramacion> builder)
    {
        builder.ToTable("SolicitudesReprogramacion");

        builder.HasKey(s => s.Id);

        builder.HasOne<Operador>().WithMany().HasForeignKey(s => s.OperadorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Envio>().WithMany().HasForeignKey(s => s.EnvioId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(s => s.Comentario).HasMaxLength(300);

        builder.HasIndex(s => new { s.EnvioId, s.FechaCreacion });
    }
}
