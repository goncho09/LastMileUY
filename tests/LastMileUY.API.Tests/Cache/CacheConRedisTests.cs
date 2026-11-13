using System.Diagnostics;
using LastMileUY.API.Infrastructure.Cache;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace LastMileUY.API.Tests.Cache;

public class CacheConRedisTests : IAsyncLifetime
{
    private static readonly CasoDeCache Caso = CasosDeCache.SeguimientoPublico;

    private record Dato(string Valor);

    private readonly RedisContainer contenedor = new RedisBuilder("redis:7.4-alpine").Build();

    public Task InitializeAsync() => contenedor.StartAsync();

    public Task DisposeAsync() => contenedor.DisposeAsync().AsTask();

    [Fact]
    public async Task Dos_instancias_de_la_API_comparten_el_cache_y_la_invalidacion()
    {
        using var redisA = await Conectar();
        using var redisB = await Conectar();
        var instanciaA = Nuevo(redisA);
        var instanciaB = Nuevo(redisB);
        var consultas = 0;

        Task<Dato?> Consultar(CancellationToken _) => Task.FromResult<Dato?>(new Dato($"v{++consultas}"));

        await instanciaA.ObtenerOCrearAsync(Caso, "seguimiento:ABC", Consultar);
        var enB = await instanciaB.ObtenerOCrearAsync(Caso, "seguimiento:ABC", Consultar);

        Assert.Equal(1, consultas);
        Assert.Equal("v1", enB!.Valor);

        await instanciaA.InvalidarAsync("seguimiento:ABC");
        var despues = await instanciaB.ObtenerOCrearAsync(Caso, "seguimiento:ABC", Consultar);

        Assert.Equal("v2", despues!.Valor);
    }

    [Fact]
    public async Task Con_Redis_caido_responde_desde_la_base_sin_esperar()
    {
        using var caido = await ConnectionMultiplexer.ConnectAsync(ConexionRedis.Opciones("127.0.0.1:1"));
        var cache = Nuevo(caido);

        var reloj = Stopwatch.StartNew();
        var dato = await cache.ObtenerOCrearAsync(Caso, "seguimiento:ABC", _ => Task.FromResult<Dato?>(new Dato("de la base")));
        await cache.InvalidarAsync("seguimiento:ABC");
        reloj.Stop();

        Assert.Equal("de la base", dato!.Valor);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(1), $"Tardó {reloj.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public async Task Con_Redis_caido_las_metricas_avisan_que_no_estan_disponibles()
    {
        using var caido = await ConnectionMultiplexer.ConnectAsync(ConexionRedis.Opciones("127.0.0.1:1"));
        var metricas = new MetricasCacheRedis(caido, NullLogger<MetricasCacheRedis>.Instance);

        metricas.Registrar(Caso, acierto: false);

        await Assert.ThrowsAsync<MetricasCacheNoDisponiblesException>(() => metricas.ObtenerAsync());
    }

    [Fact]
    public async Task Limpiar_borra_lo_cacheado_pero_no_las_metricas_ni_claves_de_otras_apps()
    {
        using var redis = await Conectar();
        var db = redis.GetDatabase();
        await db.StringSetAsync(CasosDeCache.PrefijoRedis + "seguimiento:ABC", "viejo");
        await db.StringSetAsync(CasosDeCache.PrefijoRedis + "operador:1", "viejo");
        await db.HashIncrementAsync(MetricasCacheRedis.Clave, "seguimiento:aciertos");
        await db.StringSetAsync("otra-app:clave", "no es nuestra");

        var borrados = await LimpiezaCacheRedis.LimpiarAsync(redis);

        Assert.Equal(2, borrados);
        Assert.False(await db.KeyExistsAsync(CasosDeCache.PrefijoRedis + "seguimiento:ABC"));
        Assert.False(await db.KeyExistsAsync(CasosDeCache.PrefijoRedis + "operador:1"));
        Assert.True(await db.KeyExistsAsync(MetricasCacheRedis.Clave));
        Assert.True(await db.KeyExistsAsync("otra-app:clave"));
    }

    [Fact]
    public async Task Al_volver_la_conexion_con_Redis_se_borra_el_dato_que_quedo_viejo()
    {
        using var redis = await Conectar();
        var limpieza = new LimpiezaCacheRedis(redis, NullLogger<LimpiezaCacheRedis>.Instance);
        await limpieza.StartAsync(CancellationToken.None);

        var cache = Nuevo(redis);
        await cache.ObtenerOCrearAsync(Caso, "seguimiento:ABC", _ => Task.FromResult<Dato?>(new Dato("EnDevolucion")));
        var clave = CasosDeCache.PrefijoRedis + "seguimiento:ABC";
        Assert.True(await redis.GetDatabase().KeyExistsAsync(clave));

        using var admin = await ConnectionMultiplexer.ConnectAsync(contenedor.GetConnectionString() + ",allowAdmin=true");
        await admin.GetDatabase().ExecuteAsync("CLIENT", "KILL", "TYPE", "normal", "SKIPME", "yes");

        var limite = DateTime.UtcNow.AddSeconds(15);
        while (await admin.GetDatabase().KeyExistsAsync(clave) && DateTime.UtcNow < limite)
            await Task.Delay(200);

        Assert.False(await admin.GetDatabase().KeyExistsAsync(clave));

        var dato = await cache.ObtenerOCrearAsync(Caso, "seguimiento:ABC", _ => Task.FromResult<Dato?>(new Dato("Devuelto")));
        Assert.Equal("Devuelto", dato!.Valor);

        await limpieza.StopAsync(CancellationToken.None);
    }

    private Task<ConnectionMultiplexer> Conectar() =>
        ConnectionMultiplexer.ConnectAsync(ConexionRedis.Opciones(contenedor.GetConnectionString()));

    private static CacheDistribuido Nuevo(IConnectionMultiplexer redis) =>
        new(
            new RedisCache(Options.Create(new RedisCacheOptions
            {
                ConnectionMultiplexerFactory = () => Task.FromResult(redis),
                InstanceName = CasosDeCache.PrefijoRedis
            })),
            new MetricasCacheRedis(redis, NullLogger<MetricasCacheRedis>.Instance),
            NullLogger<CacheDistribuido>.Instance);
}
