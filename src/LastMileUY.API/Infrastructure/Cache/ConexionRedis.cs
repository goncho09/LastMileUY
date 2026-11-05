using StackExchange.Redis;

namespace LastMileUY.API.Infrastructure.Cache;

// Cómo se conecta la API a Redis. Redis es un caché: si no responde, la API tiene que seguir atendiendo
// enseguida desde la base, no quedarse esperando (ver docs/cache.md, "Si Redis se cae").
public static class ConexionRedis
{
    public static ConfigurationOptions Opciones(string conexion)
    {
        var opciones = ConfigurationOptions.Parse(conexion);

        // Si Redis no está al arrancar, la API arranca igual y se reconecta sola
        opciones.AbortOnConnectFail = false;

        // Sin conexión, cada pedido a Redis falla en el acto. Lo predeterminado es dejarlo en espera
        // hasta 5 segundos por si vuelve: con una lectura y una escritura por consulta, eran ~12 s.
        opciones.BacklogPolicy = BacklogPolicy.FailFast;

        // Si Redis se cuelga sin cortar la conexión (red caída), no se espera más de un segundo
        opciones.ConnectTimeout = 2000;
        opciones.SyncTimeout = 1000;
        opciones.AsyncTimeout = 1000;

        return opciones;
    }
}
