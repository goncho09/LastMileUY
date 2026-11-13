using LastMileUY.API.Modules.Notificaciones.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class IntentoAvisoConfiguration : IEntityTypeConfiguration<IntentoAviso>
{
    public void Configure(EntityTypeBuilder<IntentoAviso> builder)
    {
        builder.ToTable("IntentosAvisos");

        builder.HasKey(i => i.Id);

        builder.HasOne<EntregaAviso>()
            .WithMany(a => a.Intentos)
            .HasForeignKey(i => i.EntregaAvisoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.EntregaAvisoId, i.NumeroIntento })
            .IsUnique();

        builder.Property(i => i.Error)
            .HasMaxLength(1000);
    }
}
