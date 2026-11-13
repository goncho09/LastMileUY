using StackExchange.Redis;

namespace LastMileUY.API.Infrastructure.Cache;

public static class ConexionRedis
{
    public static ConfigurationOptions Opciones(string conexion)
    {
        var opciones = ConfigurationOptions.Parse(conexion);

        opciones.AbortOnConnectFail = false;

        opciones.BacklogPolicy = BacklogPolicy.FailFast;

        opciones.ConnectTimeout = 2000;
        opciones.SyncTimeout = 1000;
        opciones.AsyncTimeout = 1000;

        return opciones;
    }
}
