using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Infrastructure.Correlacion;
using LastMileUY.API.Infrastructure.Mensajeria;
using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Multitenancy;

public class FabricaContextoOperador(
    IConfiguration configuracion,
    CacheDistribuido cache,
    ContextoCorrelacion correlacion,
    SenalOutbox senal)
{
    public ApplicationDbContext Crear(int operadorId)
    {
        var contexto = new ContextoOperadorFijo(operadorId);
        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(configuracion.GetConnectionString("DefaultConnection"))
            .AddInterceptors(
                new InterceptorOperadorConexion(contexto),
                new InvalidacionCacheInterceptor(cache),
                new OutboxInterceptor(correlacion, senal))
            .Options;

        return new ApplicationDbContext(opciones, contexto);
    }
}
