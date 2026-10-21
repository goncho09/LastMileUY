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
    private static readonly TimeSpan UnMinuto = TimeSpan.FromMinutes(1);

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

        var primera = await cache.ObtenerOCrearAsync("clave", Consultar, UnMinuto);
        var segunda = await cache.ObtenerOCrearAsync("clave", Consultar, UnMinuto);

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

        await cache.ObtenerOCrearAsync("clave", Consultar, UnMinuto);
        await cache.ObtenerOCrearAsync("clave", Consultar, UnMinuto);

        // Si se hubiera guardado el "no existe", un dato creado después no aparecería hasta que venza
        Assert.Equal(2, consultas);
    }

    [Fact]
    public async Task Invalidar_hace_que_la_proxima_lectura_vaya_a_la_base()
    {
        var cache = Nuevo(EnMemoria());
        var version = 0;

        Task<Dato?> Consultar(CancellationToken _) => Task.FromResult<Dato?>(new Dato($"v{++version}"));

        await cache.ObtenerOCrearAsync("clave", Consultar, UnMinuto);
        await cache.InvalidarAsync("clave");
        var despues = await cache.ObtenerOCrearAsync("clave", Consultar, UnMinuto);

        Assert.Equal("v2", despues!.Valor);
    }

    [Fact]
    public async Task Si_el_cache_no_responde_se_consulta_la_base_igual()
    {
        var cache = Nuevo(new CacheCaido());

        var dato = await cache.ObtenerOCrearAsync(
            "clave", _ => Task.FromResult<Dato?>(new Dato("de la base")), UnMinuto);

        Assert.Equal("de la base", dato!.Valor);

        // Invalidar tampoco rompe: el dato vence solo
        await cache.InvalidarAsync("clave");
    }

    [Fact]
    public void Las_claves_llevan_el_operador()
    {
        // Así un operador nunca recibe lo que se cacheó para otro (ADR-01)
        Assert.NotEqual(CasosDeCache.Operador(1), CasosDeCache.Operador(2));
        Assert.NotEqual(CasosDeCache.HojaDeRuta(1, 7), CasosDeCache.HojaDeRuta(2, 7));
    }

    private static CacheDistribuido Nuevo(IDistributedCache cache) =>
        new(cache, NullLogger<CacheDistribuido>.Instance);

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
