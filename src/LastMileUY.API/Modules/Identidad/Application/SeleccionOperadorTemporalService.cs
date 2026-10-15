using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

public sealed class SeleccionOperadorTemporalService(
    IMemoryCache cache)
{
    private static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);

    // Controla el consumo de cada token para impedir reutilizarlo.
    private readonly ConcurrentDictionary<string, byte> tokensActivos = new();

    public string Crear(string usuarioId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        cache.Set(token, usuarioId, Duracion);
        tokensActivos.TryAdd(token, 0);

        return token;
    }

    public bool IntentarConsumir(string token, out string usuarioId)
    {
        usuarioId = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        // Solo una solicitud puede consumir el token.
        if (!tokensActivos.TryRemove(token, out _))
            return false;

        if (!cache.TryGetValue(token, out string? id)
            || string.IsNullOrEmpty(id))
        {
            return false;
        }

        cache.Remove(token);
        usuarioId = id;
        return true;
    }
}