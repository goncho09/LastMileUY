using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;

namespace LastMileUY.Worker;

// Un aviso listo para mandar (lo arma DespachadorAvisos con lo que hay en EntregasAvisos)
public record AvisoPendiente(
    int Id, Guid AvisoId, string TipoEvento, string Contenido, int Intento, string UrlDestino, string? CorrelacionId);

// Exitoso: el comercio respondió 2xx. CodigoHttp es null si no llegó a responder.
public record ResultadoEnvio(bool Exitoso, int? CodigoHttp, string? Error, TimeSpan Duracion);

// Manda un aviso por HTTP al comercio: POST a la URL de la suscripción, con el evento en JSON.
// - Encabezados: X-Aviso-Id (para descartar repetidos), X-Aviso-Tipo, X-Aviso-Intento y X-Correlation-Id.
// - Cuenta como entregado solo una respuesta 2xx. Cualquier otra cosa (otro código, sin respuesta,
//   tiempo agotado, destino no permitido) es un fallo con su motivo, y nunca una excepción.
// - No sigue redirecciones: podrían llevar a una dirección interna.
public class ClienteAvisos(HttpClient http)
{
    public static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(10);

    public async Task<ResultadoEnvio> EnviarAsync(AvisoPendiente aviso, CancellationToken cancellationToken)
    {
        var reloj = Stopwatch.StartNew();

        try
        {
            using var pedido = ArmarPedido(aviso);
            using var respuesta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var codigo = (int)respuesta.StatusCode;

            return respuesta.IsSuccessStatusCode
                ? new ResultadoEnvio(true, codigo, null, reloj.Elapsed)
                : new ResultadoEnvio(false, codigo, $"El comercio respondió {codigo} {respuesta.ReasonPhrase}.", reloj.Elapsed);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResultadoEnvio(false, null, Motivo(ex), reloj.Elapsed);
        }
    }

    private static HttpRequestMessage ArmarPedido(AvisoPendiente aviso)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, aviso.UrlDestino)
        {
            Content = new StringContent(aviso.Contenido, Encoding.UTF8, "application/json")
        };

        pedido.Headers.Add(EncabezadosAvisos.AvisoId, aviso.AvisoId.ToString());
        pedido.Headers.Add(EncabezadosAvisos.Tipo, aviso.TipoEvento);
        pedido.Headers.Add(EncabezadosAvisos.Intento, aviso.Intento.ToString());

        if (!string.IsNullOrEmpty(aviso.CorrelacionId))
            pedido.Headers.Add(TopologiaMensajeria.EncabezadoCorrelacion, aviso.CorrelacionId);

        // Tarjeta "Firmar los avisos": la firma va acá, sobre el contenido y estos encabezados.

        return pedido;
    }

    private static string Motivo(Exception ex) => ex switch
    {
        TaskCanceledException => $"El comercio no respondió en {TiempoMaximo.TotalSeconds} segundos.",
        _ when Buscar<DestinoNoPermitidoException>(ex) is { } destino => destino.Message,
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "No se encontró el servidor del comercio.",
        HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => "No se pudo conectar con el comercio.",
        _ => ex.Message
    };

    private static T? Buscar<T>(Exception? ex) where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
            if (ex is T buscada)
                return buscada;

        return null;
    }

    // El manejador del HttpClient: sin redirecciones y revisando la dirección al conectarse. Revisar la
    // URL antes no alcanza: un nombre público puede resolver a una IP interna (y cambiar entre una
    // consulta y otra).
    public static SocketsHttpHandler CrearManejador(bool permitirLocales) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (contexto, cancellationToken) =>
        {
            var destino = contexto.InitialRequestMessage.RequestUri;
            var motivo = DestinosAvisos.Validar(destino?.ToString(), permitirLocales);

            if (motivo is not null)
                throw new DestinoNoPermitidoException(motivo);

            var direcciones = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, cancellationToken);
            var permitidas = direcciones.Where(ip => permitirLocales || DestinosAvisos.EsPublica(ip)).ToArray();

            if (permitidas.Length == 0)
                throw new DestinoNoPermitidoException(direcciones.Length == 0
                    ? "No se encontró el servidor del comercio."
                    : "La URL apunta a una red interna.");

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            try
            {
                await socket.ConnectAsync(permitidas, contexto.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}

public class DestinoNoPermitidoException(string motivo) : Exception($"Destino no permitido: {motivo}");
