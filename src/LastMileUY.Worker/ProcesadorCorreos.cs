using LastMileUY.Contracts.Mensajeria;
using MimeKit;

namespace LastMileUY.Worker;

public enum ResultadoCorreo
{
    Ignorado,
    YaProcesado,
    Enviado,
    SinDestinatario
}

public sealed class ConfirmacionCorreoInciertaException(string mensaje, Exception inner) : Exception(mensaje, inner);

public sealed class ProcesadorCorreos(
    RepositorioCorreos repositorio,
    IEnviadorCorreo enviador,
    IConfiguration configuracion,
    ILogger<ProcesadorCorreos> logger)
{
    private static readonly HashSet<string> EstadosTerminados = ["Enviado", "SinDestinatario"];

    public static bool CorrespondeAvisar(EstadoEnvioCambiado evento) =>
        evento.Tipo is TiposEvento.EnvioAdmitido or TiposEvento.EnvioEstadoCambiado
        && evento.EsPublico
        && PlantillaCorreoSeguimiento.Para(evento.Estado) is not null;

    public async Task<ResultadoCorreo> ProcesarAsync(EstadoEnvioCambiado evento, CancellationToken ct)
    {
        if (!CorrespondeAvisar(evento))
            return ResultadoCorreo.Ignorado;

        var reservaId = await repositorio.ReservarAsync(
            evento.EventoId, evento.EnvioId, evento.OperadorId, evento.ComercioId, ct);

        if (reservaId is null)
        {
            var estado = await repositorio.ObtenerEstadoAsync(evento.EventoId, evento.OperadorId, evento.ComercioId, ct);

            if (estado is not null && EstadosTerminados.Contains(estado))
                return ResultadoCorreo.YaProcesado;

            throw new CorreoOcupadoException($"Correo reservado por otro proceso. Estado: {estado}");
        }

        var destinatario = await repositorio.DestinatarioAsync(evento.EnvioId, evento.OperadorId, evento.ComercioId, ct);
        var motivo = destinatario is null ? "No se encontró el envío."
            : string.IsNullOrWhiteSpace(destinatario.Email) ? "El destinatario no tiene email."
            : !EmailValido(destinatario.Email) ? "El email del destinatario no es válido."
            : null;

        if (motivo is not null)
        {
            await repositorio.MarcarSinDestinatarioAsync(
                evento.EventoId, reservaId.Value, evento.OperadorId, evento.ComercioId, motivo, ct);

            logger.LogInformation("Correo del envío {EnvioId} sin enviar: {Motivo}", evento.EnvioId, motivo);
            return ResultadoCorreo.SinDestinatario;
        }

        var correo = PlantillaCorreoSeguimiento.Armar(
            new DatosCorreoSeguimiento(
                destinatario!.Email!,
                destinatario.Nombre,
                destinatario.Apellido,
                destinatario.Comercio,
                evento.CodigoSeguimiento,
                EnlaceSeguimiento(evento.CodigoSeguimiento)),
            PlantillaCorreoSeguimiento.Para(evento.Estado)!);

        try
        {
            await enviador.EnviarAsync(destinatario.Email!, correo, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            try
            {
                await repositorio.MarcarFallidoAsync(
                    evento.EventoId, reservaId.Value, evento.OperadorId, evento.ComercioId, ex.Message, ct);
            }
            catch (Exception errorRegistro)
            {
                logger.LogError(errorRegistro, "No se pudo registrar el fallo del correo {EventoId}", evento.EventoId);
            }

            throw;
        }

        try
        {
            await repositorio.MarcarEnviadoAsync(
                evento.EventoId, reservaId.Value, evento.OperadorId, evento.ComercioId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            throw new ConfirmacionCorreoInciertaException(
                "El servidor de correo aceptó el mensaje, pero no se pudo confirmar en PostgreSQL.", ex);
        }

        logger.LogInformation("Correo de {Estado} enviado para el envío {EnvioId}", evento.Estado, evento.EnvioId);
        return ResultadoCorreo.Enviado;
    }

    public static bool EmailValido(string email) =>
        MailboxAddress.TryParse(email, out var buzon)
        && buzon.Address.IndexOf('@') is > 0 and var arroba
        && arroba < buzon.Address.Length - 1
        && buzon.Address[(arroba + 1)..].Contains('.');

    private string EnlaceSeguimiento(string codigo)
    {
        var urlBase = configuracion["Correo:UrlSeguimiento"]
            ?? throw new InvalidOperationException("Falta Correo:UrlSeguimiento");
        var enlace = $"{urlBase.TrimEnd('/')}/seguimiento/{Uri.EscapeDataString(codigo)}";

        if (!Uri.TryCreate(enlace, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Correo:UrlSeguimiento debe ser una URL HTTP(S) válida.");

        return enlace;
    }
}
