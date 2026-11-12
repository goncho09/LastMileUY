using Microsoft.Extensions.Caching.Memory;

namespace LastMileUY.API.Modules.Integraciones.Application;

// Freno a las claves inválidas de la API pública, por IP. El tope de pedidos va por clave y corre
// después de autenticar, así que una clave inventada nunca llega a él: sin esto, cada intento cuesta una
// consulta a la base y se pueden probar claves sin límite (aunque adivinar una es imposible: 256 bits).
// Pasados ApiPublica:IntentosFallidosPorMinuto fallos en un minuto, esa IP recibe 429 sin consultar la
// base hasta que termina el minuto. Mientras tanto se frena todo pedido con clave desde esa IP, también
// uno con una clave buena: es el precio de no consultar la base, y dura como mucho un minuto.
// Vive en la memoria de cada instancia de la API, como los demás topes.
public sealed class IntentosFallidosClaveApi(IMemoryCache cache, IConfiguration configuracion)
{
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    private sealed class Contador(DateTimeOffset vence)
    {
        public int Fallos;
        public DateTimeOffset Vence { get; } = vence;
    }

    private int Limite => configuracion.GetValue("ApiPublica:IntentosFallidosPorMinuto", 30);

    // true si la IP ya pasó el tope; espera: cuánto falta para que se libere
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
