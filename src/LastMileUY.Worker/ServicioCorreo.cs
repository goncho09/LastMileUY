using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LastMileUY.Worker;

public interface IEnviadorCorreo
{
    Task EnviarAsync(string destinatario, CorreoArmado correo, CancellationToken cancellationToken = default);
}

public sealed class ServicioCorreo(IConfiguration configuracion) : IEnviadorCorreo
{
    public async Task EnviarAsync(string destinatario, CorreoArmado correo, CancellationToken cancellationToken = default)
    {
        var usuario = configuracion["Correo:Usuario"];
        var contrasena = configuracion["Correo:Contrasena"];
        var remitente = configuracion["Correo:Remitente"];
        var host = configuracion["Correo:Host"] is { Length: > 0 } h ? h : "smtp.gmail.com";
        var puerto = configuracion.GetValue("Correo:Puerto", 587);

        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(contrasena))
            throw new InvalidOperationException("Falta Correo:Usuario o Correo:Contrasena");
        if (string.IsNullOrWhiteSpace(remitente) || !MailboxAddress.TryParse(remitente, out var desde))
            throw new InvalidOperationException("Correo:Remitente no está configurado o no es válido.");
        if (!MailboxAddress.TryParse(destinatario, out var hacia))
            throw new InvalidOperationException("El destinatario no tiene un email válido.");

        var mensaje = new MimeMessage();
        mensaje.From.Add(desde);
        mensaje.To.Add(hacia);
        mensaje.Subject = correo.Asunto;
        mensaje.Body = new BodyBuilder { TextBody = correo.Texto, HtmlBody = correo.Html }.ToMessageBody();

        using var cliente = new SmtpClient();
        await cliente.ConnectAsync(host, puerto, SecureSocketOptions.StartTls, cancellationToken);
        await cliente.AuthenticateAsync(usuario, contrasena, cancellationToken);
        await cliente.SendAsync(new MailKit.SendRequest(mensaje), cancellationToken);
        await cliente.DisconnectAsync(true, cancellationToken);
    }
}
