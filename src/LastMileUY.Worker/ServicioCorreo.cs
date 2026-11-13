using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LastMileUY.Worker;

public sealed record DatosCorreoSeguimiento(
    string Email,
    string Nombre,
    string Apellido,
    string Comercio,
    string CodigoSeguimiento,
    string UrlSeguimiento);

public sealed class ServicioCorreo(IConfiguration configuracion)
{
    public async Task EnviarSeguimientoAsync(
        DatosCorreoSeguimiento datos,
        CancellationToken cancellationToken = default)
    {
        var nombreCompleto = string.Join(" ", new[] { datos.Nombre, datos.Apellido }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        var saludo = string.IsNullOrWhiteSpace(datos.Nombre) ? "¡Hola!" : $"¡Hola, {datos.Nombre.Trim()}!";
        var texto = $"""
            {saludo}

            Tenemos novedades sobre tu paquete. El comercio registró un envío a tu nombre.

            Código de seguimiento: {datos.CodigoSeguimiento}
            Destinatario: {nombreCompleto}
            Comercio: {datos.Comercio}
            Estado: Admitido

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
              <title>Seguimiento de tu envío - LastMileUY</title>
            </head>
            <body style="margin:0;padding:0;background:#f6f5fa;font-family:Arial,Helvetica,sans-serif;color:#1c1b22;">
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="background:#f6f5fa;">
                <tr><td align="center" style="padding:28px 12px;">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="max-width:560px;background:#ffffff;border:1px solid #e4e2ec;border-radius:12px;overflow:hidden;">
                    <tr><td style="background:#1f1e26;padding:25px 28px;">
                      <div style="font-size:23px;font-weight:700;color:#ffffff;">LastMile<span style="color:#a78bfa;">UY</span></div>
                      <div style="font-size:13px;color:#a9a6b8;margin-top:4px;">Seguimiento de envíos</div>
                    </td></tr>
                    <tr><td style="height:4px;background:#6d28d9;font-size:1px;line-height:4px;">&nbsp;</td></tr>
                    <tr><td style="padding:28px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f2ebff;border-radius:8px;">
                        <tr><td style="padding:13px 15px;color:#5b21b6;font-size:14px;font-weight:bold;">✓ Envío registrado correctamente</td></tr>
                      </table>
                      <h1 style="font-size:23px;line-height:1.3;margin:25px 0 12px;color:#1c1b22;">{{H(saludo)}}</h1>
                      <p style="font-size:15px;line-height:1.7;color:#5d5a6b;margin:0 0 24px;">Tenemos novedades sobre tu paquete. El comercio registró un envío a tu nombre y ya podés consultar su seguimiento en LastMileUY.</p>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f6f5fa;border:1px solid #e4e2ec;border-radius:9px;">
                        <tr><td style="padding:19px 18px;">
                          <div style="font-size:11px;font-weight:bold;letter-spacing:1px;color:#5d5a6b;">CÓDIGO DE SEGUIMIENTO</div>
                          <div style="font-size:21px;letter-spacing:1px;font-weight:bold;color:#6d28d9;margin:7px 0 16px;overflow-wrap:anywhere;">{{H(datos.CodigoSeguimiento)}}</div>
                          <div style="border-top:1px solid #e4e2ec;margin-bottom:14px;"></div>
                          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:14px;line-height:1.6;">
                            <tr><td style="color:#5d5a6b;padding:4px 0;">Destinatario</td><td align="right" style="font-weight:bold;color:#1c1b22;padding:4px 0;">{{H(nombreCompleto)}}</td></tr>
                            <tr><td style="color:#5d5a6b;padding:4px 0;">Comercio</td><td align="right" style="font-weight:bold;color:#1c1b22;padding:4px 0;">{{H(datos.Comercio)}}</td></tr>
                            <tr><td style="color:#5d5a6b;padding:4px 0;">Estado</td><td align="right" style="font-weight:bold;color:#6d28d9;padding:4px 0;">Admitido</td></tr>
                          </table>
                        </td></tr>
                      </table>
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:23px;">
                        <tr><td align="center" bgcolor="#6d28d9" style="border-radius:8px;">
                          <a href="{{url}}" style="display:block;padding:15px 18px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:bold;">Seguir mi envío &rarr;</a>
                        </td></tr>
                      </table>
                      <p style="text-align:center;color:#5d5a6b;font-size:12px;line-height:1.6;margin:16px 0 8px;">Consultá el estado de tu envío en cualquier momento, sin iniciar sesión.</p>
                      <p style="font-size:12px;line-height:1.6;overflow-wrap:anywhere;margin:0 0 22px;"><a href="{{url}}" style="color:#6d28d9;">{{url}}</a></p>
                      <div style="border-top:1px solid #e4e2ec;padding-top:18px;color:#5d5a6b;font-size:12px;line-height:1.6;">Este correo es una notificación automática. Si no reconocés este envío, comunicate con el comercio.</div>
                    </td></tr>
                    <tr><td align="center" style="background:#f6f5fa;padding:18px;color:#5d5a6b;font-size:12px;">
                      <strong style="color:#1c1b22;">LastMileUY</strong><br>Logística y seguimiento de envíos
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        await EnviarAsync(datos.Email, "Seguimiento de tu envío - LastMileUY", texto, html, cancellationToken);
    }

    public async Task EnviarAsync(
        string destinatario,
        string asunto,
        string contenidoTexto,
        string contenidoHtml,
        CancellationToken cancellationToken = default)
    {
        var usuario = configuracion["Correo:Usuario"];
        var contrasena = configuracion["Correo:Contrasena"];
        var remitente = configuracion["Correo:Remitente"];
        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(contrasena))
            throw new InvalidOperationException("Falta Correo:Usuario o Correo:Contrasena");
        if (string.IsNullOrWhiteSpace(remitente) || !MailboxAddress.TryParse(remitente, out var desde))
            throw new InvalidOperationException("Correo:Remitente no está configurado o no es válido.");
        if (string.IsNullOrWhiteSpace(destinatario) || !MailboxAddress.TryParse(destinatario, out var hacia))
            throw new InvalidOperationException("El destinatario del envío no tiene un email válido.");

        var mensaje = new MimeMessage();
        mensaje.From.Add(desde);
        mensaje.To.Add(hacia);
        mensaje.Subject = asunto;
        mensaje.Body = new BodyBuilder { TextBody = contenidoTexto, HtmlBody = contenidoHtml }.ToMessageBody();

        using var cliente = new SmtpClient();
        await cliente.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, cancellationToken);
        await cliente.AuthenticateAsync(usuario, contrasena, cancellationToken);
        await cliente.SendAsync(new MailKit.SendRequest(mensaje), cancellationToken);
        await cliente.DisconnectAsync(true, cancellationToken);
    }
}
