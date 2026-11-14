using System.Net;

namespace LastMileUY.Worker;

public sealed record DatosCorreoSeguimiento(
    string Email,
    string Nombre,
    string Apellido,
    string Comercio,
    string CodigoSeguimiento,
    string UrlSeguimiento);

public sealed record NovedadEnvio(string Estado, string Asunto, string Aviso, string Mensaje);

public sealed record CorreoArmado(string Asunto, string Texto, string Html);

public static class PlantillaCorreoSeguimiento
{
    private static readonly IReadOnlyDictionary<string, NovedadEnvio> Novedades = new Dictionary<string, NovedadEnvio>
    {
        ["Admitido"] = new("Admitido", "Registramos tu envío",
            "Envío registrado",
            "El comercio registró un envío a tu nombre y ya podés consultar su seguimiento."),
        ["EnTransito"] = new("En camino", "Tu envío está en camino",
            "Salió a reparto",
            "Tu envío salió a reparto y va camino a la dirección de entrega."),
        ["Entregado"] = new("Entregado", "Entregamos tu envío",
            "Envío entregado",
            "Tu envío fue entregado. Gracias por elegirnos."),
        ["EntregadoParcialmente"] = new("Entregado en parte", "Entregamos parte de tu envío",
            "Entrega parcial",
            "Entregamos una parte de los bultos. En el seguimiento podés ver qué pasa con el resto."),
        ["NoEntregado"] = new("No entregado", "No pudimos entregar tu envío",
            "Intento de entrega sin éxito",
            "Pasamos por la dirección pero no pudimos entregar el envío. En el seguimiento vas a ver cómo sigue."),
        ["Reprogramado"] = new("Reprogramado", "Reprogramamos la entrega de tu envío",
            "Nueva visita",
            "Vamos a volver a intentar la entrega. Seguí el envío para ver la novedad."),
        ["EnDevolucion"] = new("En devolución", "Tu envío vuelve al comercio",
            "Devolución en curso",
            "No se pudo completar la entrega y el envío vuelve al comercio. Comunicate con el comercio por cualquier duda."),
        ["Devuelto"] = new("Devuelto", "Tu envío volvió al comercio",
            "Envío devuelto",
            "El envío ya está de vuelta en el comercio. Comunicate con el comercio por cualquier duda.")
    };

    public static NovedadEnvio? Para(string estado) => Novedades.GetValueOrDefault(estado);

    public static CorreoArmado Armar(DatosCorreoSeguimiento datos, NovedadEnvio novedad)
    {
        var nombreCompleto = string.Join(" ", new[] { datos.Nombre, datos.Apellido }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        var saludo = string.IsNullOrWhiteSpace(datos.Nombre) ? "Hola" : $"Hola, {datos.Nombre.Trim()}";
        var asunto = $"{novedad.Asunto} - LastMileUY";

        var texto = $"""
            {saludo}

            {novedad.Mensaje}

            Código de seguimiento: {datos.CodigoSeguimiento}
            Destinatario: {nombreCompleto}
            Comercio: {datos.Comercio}
            Estado: {novedad.Estado}

            Seguí tu envío: {datos.UrlSeguimiento}

            LastMileUY - Seguimiento de envíos
            Si no reconocés este envío, comunicate con el comercio.
            """;

        static string H(string? valor) => WebUtility.HtmlEncode(valor ?? string.Empty);
        var url = H(datos.UrlSeguimiento);

        var html = $$"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{H(asunto)}}</title>
            </head>
            <body style="margin:0;padding:0;background:#f6f5fa;font-family:Arial,Helvetica,sans-serif;color:#1c1b22;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="background:#f6f5fa;">
                <tr><td align="center" style="padding:28px 12px;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="max-width:560px;background:#ffffff;border:1px solid #e4e2ec;border-radius:8px;overflow:hidden;">
                    <tr><td style="background:#1f1e26;padding:22px 28px;">
                      <div style="font-size:20px;font-weight:700;color:#ffffff;">LastMileUY</div>
                      <div style="font-size:13px;color:#a9a6b8;margin-top:4px;">Seguimiento de envíos</div>
                    </td></tr>
                    <tr><td style="padding:28px;">
                      <div style="font-size:13px;font-weight:bold;color:#4d3fa0;">{{H(novedad.Aviso)}}</div>
                      <h1 style="font-size:22px;line-height:1.3;margin:8px 0 12px;color:#1c1b22;">{{H(saludo)}}</h1>
                      <p style="font-size:15px;line-height:1.7;color:#5d5a6b;margin:0 0 24px;">{{H(novedad.Mensaje)}}</p>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f5fa;border:1px solid #e4e2ec;border-radius:6px;">
                        <tr><td style="padding:18px;">
                          <div style="font-size:12px;color:#5d5a6b;">Código de seguimiento</div>
                          <div style="font-size:20px;letter-spacing:1px;font-weight:bold;color:#1c1b22;margin:6px 0 14px;overflow-wrap:anywhere;">{{H(datos.CodigoSeguimiento)}}</div>
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:14px;line-height:1.6;border-top:1px solid #e4e2ec;">
                            <tr><td style="color:#5d5a6b;padding:8px 0 4px;">Destinatario</td><td align="right" style="color:#1c1b22;padding:8px 0 4px;">{{H(nombreCompleto)}}</td></tr>
                            <tr><td style="color:#5d5a6b;padding:4px 0;">Comercio</td><td align="right" style="color:#1c1b22;padding:4px 0;">{{H(datos.Comercio)}}</td></tr>
                            <tr><td style="color:#5d5a6b;padding:4px 0;">Estado</td><td align="right" style="font-weight:bold;color:#4d3fa0;padding:4px 0;">{{H(novedad.Estado)}}</td></tr>
                          </table>
                        </td></tr>
                      </table>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:22px;">
                        <tr><td align="center" bgcolor="#4d3fa0" style="border-radius:6px;">
                          <a href="{{url}}" style="display:block;padding:14px 18px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:bold;">Seguir mi envío</a>
                        </td></tr>
                      </table>
                      <p style="font-size:12px;line-height:1.6;overflow-wrap:anywhere;margin:16px 0 22px;color:#5d5a6b;">Si el botón no funciona, copiá este enlace: <a href="{{url}}" style="color:#4d3fa0;">{{url}}</a></p>
                      <div style="border-top:1px solid #e4e2ec;padding-top:18px;color:#5d5a6b;font-size:12px;line-height:1.6;">Este correo es una notificación automática. Si no reconocés este envío, comunicate con el comercio.</div>
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        return new CorreoArmado(asunto, texto, html);
    }
}
