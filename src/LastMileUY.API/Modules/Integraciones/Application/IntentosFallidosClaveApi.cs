using Microsoft.Extensions.Caching.Memory;

namespace LastMileUY.API.Modules.Integraciones.Application;

public sealed class IntentosFallidosClaveApi(IMemoryCache cache, IConfiguration configuracion)
{
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    private sealed class Contador(DateTimeOffset vence)
    {
        public int Fallos;
        public DateTimeOffset Vence { get; } = vence;
    }

    private int Limite => configuracion.GetValue("ApiPublica:IntentosFallidosPorMinuto", 30);

    public bool EstaBloqueada(string ip, out TimeSpan espera)
    {
        espera = TimeSpan.Zero;

        if (!cache.TryGetValue(Clave(ip), out Contador? contador) || contador is null)
            return false;

        if (Volatile.Read(ref contador.Fallos) < Limite)
            return false;

        espera = contador.Vence - DateTimeOffset.UtcNow;
        return espera > TimeSpan.Zero;
    }

    public void RegistrarFallo(string ip)
    {
        var contador = cache.GetOrCreate(Clave(ip), entrada =>
        {
            var vence = DateTimeOffset.UtcNow.Add(Ventana);
            entrada.AbsoluteExpiration = vence;
            return new Contador(vence);
        })!;

        Interlocked.Increment(ref contador.Fallos);
    }

    private static string Clave(string ip) => "api-publica:fallos:" + ip;
}
