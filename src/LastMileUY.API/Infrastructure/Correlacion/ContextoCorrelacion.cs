using System.Text.RegularExpressions;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.API.Infrastructure.Correlacion;

// Identificador de correlación del pedido actual: une en los registros (logs) el pedido HTTP, los
// eventos que publica y lo que hace el worker con ellos. Uno por pedido (scoped); lo carga el
// middleware de abajo y lo lee el outbox al guardar los eventos.
public class ContextoCorrelacion
{
    public string? Id { get; set; }
}

public static partial class CorrelacionMiddleware
{
    // Si el cliente manda uno, se respeta para seguir su pedido de punta a punta. Solo caracteres
    // seguros y largo acotado: termina en los registros y en los mensajes.
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
