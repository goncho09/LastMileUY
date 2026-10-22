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
    // con cada entrega o intento fallido. Lo que la mantiene al día es la invalidación
    // (InvalidacionCacheInterceptor la borra en cuanto cambia algo de la ruta); la expiración
    // es solo la red de seguridad para cambios que no pasen por EF (SQL directo, ExecuteUpdate).
    // Con 1 minuto casi siempre vencía entre una consulta y otra del repartidor: 10 minutos
    // aprovecha el caché y limita el peor caso de dato viejo a 10 minutos.
    public static readonly TimeSpan DuracionHojaDeRuta = TimeSpan.FromMinutes(10);

    public static string HojaDeRuta(int operadorId, int rutaId) => $"hoja:op:{operadorId}:ruta:{rutaId}";
}
