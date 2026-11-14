using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Deposito.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class RecepcionDepositoConfiguration : IEntityTypeConfiguration<RecepcionDeposito>
{
    public void Configure(EntityTypeBuilder<RecepcionDeposito> builder)
    {
        builder.ToTable("RecepcionesDeposito");

        builder.HasKey(r => r.Id);

        builder.HasOne<Operador>().WithMany().HasForeignKey(r => r.OperadorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Comercio>().WithMany().HasForeignKey(r => r.ComercioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Envio>().WithMany().HasForeignKey(r => r.EnvioId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(r => r.ResponsableId).HasMaxLength(450);
        builder.Property(r => r.ResueltaPor).HasMaxLength(450);
        builder.Property(r => r.Observaciones).HasMaxLength(500);
        builder.Property(r => r.Resolucion).HasMaxLength(500);

        builder.HasMany(r => r.Bultos)
            .WithOne()
            .HasForeignKey(b => b.RecepcionDepositoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.EnvioId);

        builder.HasIndex(r => new { r.OperadorId, r.FechaHora })
            .HasFilter("\"TieneDiscrepancias\" AND \"FechaResolucion\" IS NULL");
    }
}

public class RecepcionBultoConfiguration : IEntityTypeConfiguration<RecepcionBulto>
{
    public void Configure(EntityTypeBuilder<RecepcionBulto> builder)
    {
        builder.ToTable("RecepcionesBulto");

        builder.HasKey(b => b.Id);

        builder.HasOne<Bulto>().WithMany().HasForeignKey(b => b.BultoId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(b => b.PesoDeclaradoKg).HasPrecision(10, 2);
        builder.Property(b => b.PesoMedidoKg).HasPrecision(10, 2);

        builder.HasIndex(b => new { b.RecepcionDepositoId, b.BultoId }).IsUnique();
    }
}
