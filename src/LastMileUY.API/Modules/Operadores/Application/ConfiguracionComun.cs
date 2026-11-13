using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Operadores.Application;

public class ConflictoConfiguracionException(string mensaje) : Exception(mensaje);

internal static class ConfiguracionComun
{
    public static int OperadorActual(this IContextoOperador contexto) =>
        contexto.OperadorId ?? throw new UnauthorizedAccessException("No se pudo identificar el operador.");

    public static string Requerido(string? valor, string campo, int largoMaximo)
    {
        var limpio = valor?.Trim();

        if (string.IsNullOrEmpty(limpio))
            throw new ArgumentException($"Completá {campo}.");

        if (limpio.Length > largoMaximo)
            throw new ArgumentException($"{Mayuscula(campo)} puede tener hasta {largoMaximo} caracteres.");

        return limpio;
    }

    public static string? Opcional(string? valor, string campo, int largoMaximo)
    {
        var limpio = valor?.Trim();

        if (string.IsNullOrEmpty(limpio))
            return null;

        if (limpio.Length > largoMaximo)
            throw new ArgumentException($"{Mayuscula(campo)} puede tener hasta {largoMaximo} caracteres.");

        return limpio;
    }

    public static decimal Cantidad(decimal valor, string campo, int decimales, decimal maximo, bool permiteCero = false)
    {
        if (valor < 0 || (!permiteCero && valor == 0))
            throw new ArgumentException($"{Mayuscula(campo)} tiene que ser mayor que {(permiteCero ? "o igual a " : "")}cero.");

        if (valor > maximo)
            throw new ArgumentException($"{Mayuscula(campo)} no puede pasar de {maximo}.");

        if (decimal.Round(valor, decimales) != valor)
            throw new ArgumentException($"{Mayuscula(campo)} admite hasta {decimales} decimales.");

        return valor;
    }

    public static int Entero(int valor, string campo, int minimo, int maximo)
    {
        if (valor < minimo || valor > maximo)
            throw new ArgumentException($"{Mayuscula(campo)} tiene que estar entre {minimo} y {maximo}.");

        return valor;
    }

    public static ModalidadEnvio Modalidad(string? valor)
    {
        if (!Enum.TryParse<ModalidadEnvio>(valor, ignoreCase: true, out var modalidad) || !Enum.IsDefined(modalidad))
            throw new ArgumentException("La modalidad tiene que ser Estandar o Urgente.");

        return modalidad;
    }

    public static async Task GuardarAsync(this ApplicationDbContext db, string siEstaRepetido, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictoConfiguracionException(siEstaRepetido);
        }
    }

    private static string Mayuscula(string texto) =>
        texto.Length == 0 ? texto : char.ToUpperInvariant(texto[0]) + texto[1..];
}
