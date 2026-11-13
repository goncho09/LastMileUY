namespace LastMileUY.API.Infrastructure.Cache;

public record CasoDeCache(string Id, string Nombre, string Perfil, TimeSpan Duracion);

public static class CasosDeCache
{
    public const string PrefijoRedis = "lastmileuy:";

    public static readonly CasoDeCache ConfiguracionOperador = new(
        "operador",
        "Configuración del operador",
        "Se lee seguido y casi nunca cambia",
        TimeSpan.FromHours(1));

    public static readonly CasoDeCache SeguimientoPublico = new(
        "seguimiento",
        "Seguimiento público",
        "Mucho tráfico y el estado cambia pocas veces",
        TimeSpan.FromMinutes(2));

    public static readonly IReadOnlyList<CasoDeCache> Todos = [ConfiguracionOperador, SeguimientoPublico];

    public static string ClaveOperador(int operadorId) => $"operador:{operadorId}";

    public static string ClaveSeguimiento(string codigoSeguimiento) => $"seguimiento:{codigoSeguimiento}";
}
