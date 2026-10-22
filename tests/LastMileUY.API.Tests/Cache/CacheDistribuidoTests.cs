namespace LastMileUY.API.Tests.Cache;

using LastMileUY.API.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// El caché común (CacheDistribuido) sin Redis: con el caché en memoria, que tiene la misma interfaz,
// y con uno que falla siempre, como un Redis caído.
public class CacheDistribuidoTests
{
    // Un caso de prueba que dura un minuto (en la API los casos están en CasosDeCache)
    private static readonly CasoDeCache Caso = CasosDeCache.ConfiguracionOperador with { Duracion = TimeSpan.FromMinutes(1) };

    private record Dato(string Valor);

    [Fact]
    public async Task La_segunda_lectura_sale_del_cache_sin_ir_a_la_base()
    {
        var cache = Nuevo(EnMemoria());
        var consultas = 0;

        Task<Dato?> Consultar(CancellationToken _)
        {
            consultas++;
            return Task.FromResult<Dato?>(new Dato("de la base"));
        }

        var primera = await cache.ObtenerOCrearAsync(Caso, "clave", Consultar);
        var segunda = await cache.ObtenerOCrearAsync(Caso, "clave", Consultar);

        Assert.Equal(1, consultas);
        Assert.Equal(primera, segunda);
    }

    [Fact]
    public async Task No_guarda_lo_que_no_existe()
    {
        var cache = Nuevo(EnMemoria());
        var consultas = 0;

        Task<Dato?> Consultar(CancellationToken _)
        {
            consultas++;
            return Task.FromResult<Dato?>(null);
        }

        await cache.ObtenerOCrearAsync(Caso, "clave", Consultar);
        await cache.ObtenerOCrearAsync(Caso, "clave", Consultar);

        // Si se hubiera guardado el "no existe", un dato creado después no aparecería hasta que venza
        Assert.Equal(2, consultas);
    }

    [Fact]
    public async Task Invalidar_hace_que_la_proxima_lectura_vaya_a_la_base()
    {
        var cache = Nuevo(EnMemoria());
        var version = 0;

        Task<Dato?> Consultar(CancellationToken _) => Task.FromResult<Dato?>(new Dato($"v{++version}"));

        await cache.ObtenerOCrearAsync(Caso, "clave", Consultar);
        await cache.InvalidarAsync("clave");
        var despues = await cache.ObtenerOCrearAsync(Caso, "clave", Consultar);

        Assert.Equal("v2", despues!.Valor);
    }

    [Fact]
    public async Task Si_el_cache_no_responde_se_consulta_la_base_igual()
    {
        var cache = Nuevo(new CacheCaido());

        var dato = await cache.ObtenerOCrearAsync(
            Caso, "clave", _ => Task.FromResult<Dato?>(new Dato("de la base")));

        Assert.Equal("de la base", dato!.Valor);

        // Invalidar tampoco rompe: el dato vence solo
        await cache.InvalidarAsync("clave");
    }

    [Fact]
    public void Las_claves_llevan_el_operador()
    {
        // Así un operador nunca recibe lo que se cacheó para otro (ADR-01)
        Assert.NotEqual(CasosDeCache.ClaveOperador(1), CasosDeCache.ClaveOperador(2));
    }

    [Fact]
    public async Task Cuenta_un_fallo_cuando_va_a_la_base_y_un_acierto_cuando_sale_del_cache()
    {
        var metricas = new MetricasCacheMemoria();
        var cache = Nuevo(EnMemoria(), metricas);

        Task<Dato?> Consultar(CancellationToken _) => Task.FromResult<Dato?>(new Dato("de la base"));

        await cache.ObtenerOCrearAsync(Caso, "clave", Consultar); // no estaba: fallo
        await cache.ObtenerOCrearAsync(Caso, "clave", Consultar); // estaba: acierto
        await cache.ObtenerOCrearAsync(Caso, "clave", Consultar); // estaba: acierto

        var (aciertos, fallos) = (await metricas.ObtenerAsync())[Caso.Id];
        Assert.Equal(2, aciertos);
        Assert.Equal(1, fallos);
    }

    [Fact]
    public async Task Si_el_cache_no_responde_cuenta_como_fallo()
    {
        var metricas = new MetricasCacheMemoria();
        var cache = Nuevo(new CacheCaido(), metricas);

        await cache.ObtenerOCrearAsync(Caso, "clave", _ => Task.FromResult<Dato?>(new Dato("de la base")));

        var (aciertos, fallos) = (await metricas.ObtenerAsync())[Caso.Id];
        Assert.Equal(0, aciertos);
        Assert.Equal(1, fallos);
    }

    [Fact]
    public async Task Reiniciar_pone_los_contadores_en_cero()
    {
        var metricas = new MetricasCacheMemoria();
        metricas.Registrar(Caso, acierto: true);
        metricas.Registrar(Caso, acierto: false);

        await metricas.ReiniciarAsync();

        Assert.Equal((0L, 0L), (await metricas.ObtenerAsync())[Caso.Id]);
    }

    private static CacheDistribuido Nuevo(IDistributedCache cache, IMetricasCache? metricas = null) =>
        new(cache, metricas ?? new MetricasCacheMemoria(), NullLogger<CacheDistribuido>.Instance);

    private static MemoryDistributedCache EnMemoria() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    // Como un Redis caído: toda operación falla
    private class CacheCaido : IDistributedCache
    {
        public byte[]? Get(string key) => throw Caido();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Caido();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Caido();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Caido();
        public void Refresh(string key) => throw Caido();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Caido();
        public void Remove(string key) => throw Caido();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Caido();

        private static Exception Caido() => new InvalidOperationException("Redis no responde");
    }
}
