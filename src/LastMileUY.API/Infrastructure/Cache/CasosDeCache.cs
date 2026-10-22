namespace LastMileUY.API.Infrastructure.Cache;

// Un caso cacheado: su identificador (para claves y métricas), cómo se muestra en el tablero,
// por qué se cachea y cuánto dura cada dato en el caché.
public record CasoDeCache(string Id, string Nombre, string Perfil, TimeSpan Duracion);

// Los casos cacheados y sus claves (ver ADR de caché). Para sumar uno nuevo: agregarlo acá y en Todos,
// y envolver su consulta con CacheDistribuido.ObtenerOCrearAsync.
public static class CasosDeCache
{
    // Configuración del operador (nombre, color, logo y reglas operativas): se pide en cada inicio de
    // sesión y en la página del operador, y casi nunca cambia -> duración larga.
    public static readonly CasoDeCache ConfiguracionOperador = new(
        "operador",
        "Configuración del operador",
        "Se lee seguido y casi nunca cambia",
        TimeSpan.FromHours(1));

    // Seguimiento público: el componente de más tráfico (lo consulta cualquiera con el código, sin sesión)
    // y el estado cambia pocas veces en el viaje del paquete -> muchas lecturas por cada cambio.
    // Duración corta porque es lo que ve el destinatario; la invalidación lo actualiza en cuanto cambia el envío.
    public static readonly CasoDeCache SeguimientoPublico = new(
        "seguimiento",
        "Seguimiento público",
        "Mucho tráfico y el estado cambia pocas veces",
        TimeSpan.FromMinutes(2));

    // En el orden en que se muestran en el tablero técnico
    public static readonly IReadOnlyList<CasoDeCache> Todos = [ConfiguracionOperador, SeguimientoPublico];

    // Las claves de los datos de un operador llevan su Id: así nunca se recibe un dato cacheado de otro (ADR-01).
    public static string ClaveOperador(int operadorId) => $"operador:{operadorId}";

    // Sin operador: el código de seguimiento ya identifica un único envío
    public static string ClaveSeguimiento(string codigoSeguimiento) => $"seguimiento:{codigoSeguimiento}";
}
