using LastMileUY.API.Infrastructure.Cache;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Seguimiento.Application;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Seguimiento;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LastMileUY.API.Tests.Multitenancy;

using static BaseConDosOperadores;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AvanceBultosSeguimientoTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Muestra_cuantos_bultos_se_entregaron_de_cuantos()
    {
        var (envioId, codigo) = await EnvioEnTransitoAsync(bultos: 3);

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var envio = await db.Envios.Include(e => e.Bultos).SingleAsync(e => e.Id == envioId);
            var bultos = envio.Bultos.OrderBy(b => b.Id).ToList();
            var motivo = await MotivoAsync(db);

            envio.RegistrarResultadoBultos(
            [
                Resultado(bultos[0], entregado: true),
                Resultado(bultos[1], entregado: true),
                Resultado(bultos[2], entregado: false, motivo)
            ]);
            await db.SaveChangesAsync();
        }

        var seguimiento = await SeguimientoAsync(codigo);

        Assert.Equal("EntregadoParcialmente", seguimiento.Estado);
        Assert.Equal(new AvanceBultosDto(Total: 3, Entregados: 2, Devueltos: 0), seguimiento.Bultos);
        Assert.True(seguimiento.Bultos!.EntregaParcial);
    }

    [Fact]
    public async Task Antes_de_la_entrega_no_hay_ninguno_entregado()
    {
        var (_, codigo) = await EnvioEnTransitoAsync(bultos: 2);

        var seguimiento = await SeguimientoAsync(codigo);

        Assert.Equal(new AvanceBultosDto(Total: 2, Entregados: 0, Devueltos: 0), seguimiento.Bultos);
        Assert.False(seguimiento.Bultos!.EntregaParcial);
    }

    [Fact]
    public async Task Un_cambio_de_un_bulto_invalida_el_seguimiento_en_el_cache()
    {
        var (envioId, codigo) = await EnvioEnTransitoAsync(bultos: 2);
        var cache = new CacheDistribuido(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            new MetricasCacheMemoria(),
            NullLogger<CacheDistribuido>.Instance);
        var clave = CasosDeCache.ClaveSeguimiento(codigo);
        var consultas = 0;

        async Task<SeguimientoPublicoDto?> Consultar(CancellationToken ct)
        {
            consultas++;
            return await SeguimientoAsync(codigo);
        }

        await cache.ObtenerOCrearAsync(CasosDeCache.SeguimientoPublico, clave, Consultar);

        await using (var db = ContextoConCache(cache))
        {
            var bulto = await db.Bultos.Where(b => b.EnvioId == envioId).OrderBy(b => b.Id).FirstAsync();
            bulto.CambiarEstado(EstadoBulto.Extraviado);
            await db.SaveChangesAsync();
        }

        await cache.ObtenerOCrearAsync(CasosDeCache.SeguimientoPublico, clave, Consultar);

        Assert.Equal(2, consultas);
    }

    private ApplicationDbContext ContextoConCache(CacheDistribuido cache)
    {
        var contexto = new ContextoOperadorFijo(OperadorA);
        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(baseDeDatos.CadenaAplicacion)
            .AddInterceptors(new InterceptorOperadorConexion(contexto), new InvalidacionCacheInterceptor(cache))
            .Options;

        return new ApplicationDbContext(opciones, contexto);
    }

    private async Task<SeguimientoPublicoDto> SeguimientoAsync(string codigo)
    {
        await using var db = baseDeDatos.CrearContexto(null);
        return Assert.IsType<SeguimientoPublicoDto>(await new SeguimientoPublicoService(db, new FuncionalidadesService(db)).ObtenerAsync(codigo));
    }

    private async Task<(int Id, string Codigo)> EnvioEnTransitoAsync(int bultos)
    {
        var (_, _, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: bultos);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var codigo = await db.Envios.Where(e => e.Id == envios[0]).Select(e => e.CodigoSeguimiento).SingleAsync();

        return (envios[0], codigo);
    }

    private static ResultadoBultoIntento Resultado(Bulto bulto, bool entregado, int? motivo = null) => new()
    {
        BultoId = bulto.Id,
        Entregado = entregado,
        MotivoNoEntregaId = motivo
    };

    private static async Task<int> MotivoAsync(ApplicationDbContext db)
    {
        const string nombre = "Bulto dañado (seguimiento)";
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Nombre == nombre);

        if (motivo is null)
        {
            motivo = new MotivoNoEntrega { Nombre = nombre };
            db.MotivosNoEntrega.Add(motivo);
            await db.SaveChangesAsync();
        }

        return motivo.Id;
    }
}
