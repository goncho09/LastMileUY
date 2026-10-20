namespace LastMileUY.API.Infrastructure.Cache;

// Los casos cacheados: su clave y cuánto dura cada uno (ver ADR de caché).
// Las claves SIEMPRE llevan el operador: así un operador nunca recibe datos cacheados de otro (ADR-01).
public static class CasosDeCache
{
    // Configuración del operador (nombre, color, logo y reglas operativas):
    // se lee en cada pantalla y casi nunca cambia -> duración larga.
    public static readonly TimeSpan DuracionOperador = TimeSpan.FromHours(1);

    public static string Operador(int operadorId) => $"operador:{operadorId}";

    // Hoja de ruta del repartidor: se descarga varias veces en la jornada y cambia
    // con cada entrega o intento fallido -> duración corta.
    public static readonly TimeSpan DuracionHojaDeRuta = TimeSpan.FromMinutes(1);

    public static string HojaDeRuta(int operadorId, int rutaId) => $"hoja:op:{operadorId}:ruta:{rutaId}";
}
