using LastMileUY.API.Modules.Comercios.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LastMileUY.API.Infrastructure.Persistence.Configurations;

public class OperadorComercioConfiguration
    : IEntityTypeConfiguration<OperadorComercio>
{
    public void Configure(EntityTypeBuilder<OperadorComercio> builder)
    {
        builder.ToTable("OperadoresComercios");

        builder.HasKey(oc => new
        {
            oc.OperadorId,
            oc.ComercioId
        });
    }
}