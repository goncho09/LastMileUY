
using System.Text.Json;
using LastMileUY.Contracts.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace LastMileUY.CommercePortal.Servicios;

public sealed class SeleccionOperadorCookie(IDataProtectionProvider provider)
{
    private const string NombreCookie = "LastMileUY.SeleccionOperador";

    private readonly ITimeLimitedDataProtector protector =
        provider.CreateProtector("LastMileUY.SeleccionOperador.v1")
            .ToTimeLimitedDataProtector();

    public void Guardar(
        HttpContext contexto,
        string token,
        List<OperadorDisponibleDto> operadores)
    {
        var datos = JsonSerializer.Serialize(new DatosSeleccion(token, operadores));
        var protegido = protector.Protect(datos, TimeSpan.FromMinutes(5));

        contexto.Response.Cookies.Append(NombreCookie, protegido,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,
                MaxAge = TimeSpan.FromMinutes(5),
                Path = "/"
            });
    }

    public DatosSeleccion? Leer(HttpContext contexto)
    {
        if (!contexto.Request.Cookies.TryGetValue(NombreCookie, out var valor))
            return null;

        try
        {
            var datos = protector.Unprotect(valor);
            return JsonSerializer.Deserialize<DatosSeleccion>(datos);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Eliminar(HttpContext contexto)
    {
        contexto.Response.Cookies.Delete(NombreCookie,
            new CookieOptions
            {
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
    }

    public sealed record DatosSeleccion(
        string TokenTemporal,
        List<OperadorDisponibleDto> Operadores);
}
