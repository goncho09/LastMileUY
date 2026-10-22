using System.Collections.Concurrent;
using StackExchange.Redis;

namespace LastMileUY.API.Infrastructure.Cache;

// Aciertos y fallos del caché por caso (ver ADR de caché y el tablero técnico del Backoffice).
//   - Acierto: el dato estaba en el caché y no hubo que ir a la base.
//   - Fallo: no estaba (primera vez, venció o se invalidó) y se consultó la base.
// Son números técnicos de toda la plataforma (no tienen datos de ningún operador).
public interface IMetricasCache
{
    // No espera respuesta: contar nunca demora ni rompe el pedido que se está atendiendo
    void Registrar(CasoDeCache caso, bool acierto);

    Task<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>> ObtenerAsync(CancellationToken cancellationToken = default);

    Task ReiniciarAsync(CancellationToken cancellationToken = default);
}

// Con Redis: los contadores viven en Redis, así todas las instancias de la API suman en el mismo lugar.
// HINCRBY suma de a uno y es atómico: dos pedidos a la vez no pisan la cuenta.
public class MetricasCacheRedis(IConnectionMultiplexer redis, ILogger<MetricasCacheRedis> logger) : IMetricasCache
{
    private const string Clave = "lastmileuy:metricas:cache";

    public void Registrar(CasoDeCache caso, bool acierto)
    {
        try
        {
            // FireAndForget: se manda y no se espera la respuesta
            redis.GetDatabase().HashIncrement(Clave, Campo(caso.Id, acierto), 1, CommandFlags.FireAndForget);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo registrar la métrica del caché {Caso}", caso.Id);
        }
    }

    public async Task<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>> ObtenerAsync(
        CancellationToken cancellationToken = default)
    {
        var campos = (await redis.GetDatabase().HashGetAllAsync(Clave))
            .ToDictionary(c => c.Name.ToString(), c => (long)c.Value);

        return CasosDeCache.Todos.ToDictionary(
            caso => caso.Id,
            caso => (campos.GetValueOrDefault(Campo(caso.Id, true)), campos.GetValueOrDefault(Campo(caso.Id, false))));
    }

    public Task ReiniciarAsync(CancellationToken cancellationToken = default) =>
        redis.GetDatabase().KeyDeleteAsync(Clave);

    private static string Campo(string casoId, bool acierto) => $"{casoId}:{(acierto ? "aciertos" : "fallos")}";
}

// Sin Redis (pruebas, o la API corriendo fuera de Docker): en memoria de esta instancia
public class MetricasCacheMemoria : IMetricasCache
{
    private readonly ConcurrentDictionary<string, long> aciertos = new();
    private readonly ConcurrentDictionary<string, long> fallos = new();

    public void Registrar(CasoDeCache caso, bool acierto) =>
        (acierto ? aciertos : fallos).AddOrUpdate(caso.Id, 1, (_, actual) => actual + 1);

    public Task<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>> ObtenerAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, (long Aciertos, long Fallos)>>(
            CasosDeCache.Todos.ToDictionary(
                caso => caso.Id,
                caso => (aciertos.GetValueOrDefault(caso.Id), fallos.GetValueOrDefault(caso.Id))));

    public Task ReiniciarAsync(CancellationToken cancellationToken = default)
    {
        aciertos.Clear();
        fallos.Clear();
        return Task.CompletedTask;
    }
}
