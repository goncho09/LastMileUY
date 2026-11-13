using System.Text.RegularExpressions;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.API.Infrastructure.Correlacion;

public class ContextoCorrelacion
{
    public string? Id { get; set; }
}

public static partial class CorrelacionMiddleware
{
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex FormatoValido();

    public static IApplicationBuilder UseCorrelacion(this IApplicationBuilder app) =>
        app.Use(async (contexto, siguiente) =>
        {
            var recibido = contexto.Request.Headers[TopologiaMensajeria.EncabezadoCorrelacion].ToString();
            var id = FormatoValido().IsMatch(recibido) ? recibido : Guid.NewGuid().ToString("N");

            contexto.RequestServices.GetRequiredService<ContextoCorrelacion>().Id = id;
            contexto.Response.Headers[TopologiaMensajeria.EncabezadoCorrelacion] = id;

            var logger = contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Correlacion");
            using (logger.BeginScope("Correlación {CorrelacionId}", id))
            {
                await siguiente(contexto);
            }
        });
}
