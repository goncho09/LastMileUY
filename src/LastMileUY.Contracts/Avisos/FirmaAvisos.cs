
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LastMileUY.Contracts.Avisos;

public static class FirmaAvisos
{
    public const string EncabezadoTimestamp = "X-Aviso-Timestamp";
    public const string EncabezadoFirma = "X-Aviso-Firma";
    public const string EncabezadoSuscripcionId = "X-Aviso-Suscripcion-Id";

    public static string Generar(
        string claveHex,
        int suscripcionId,
        Guid avisoId,
        long timestamp,
        string contenido)
    {
        var clave = Convert.FromHexString(claveHex);

        if (clave.Length != 32)
            throw new ArgumentException(
                "La clave de firma debe tener 32 bytes.");

        var mensaje = string.Join("\n",
            suscripcionId.ToString(CultureInfo.InvariantCulture),
            avisoId.ToString(),
            timestamp.ToString(CultureInfo.InvariantCulture),
            contenido);

        var firma = HMACSHA256.HashData(
            clave,
            Encoding.UTF8.GetBytes(mensaje));

        return $"sha256={Convert.ToHexStringLower(firma)}";
    }

    public static bool Verificar(
        string claveHex,
        int suscripcionId,
        Guid avisoId,
        long timestamp,
        string contenido,
        string firmaRecibida)
    {
        try
        {
            if (!firmaRecibida.StartsWith(
                    "sha256=", StringComparison.Ordinal))
                return false;

            var firmaEsperada = Generar(
                claveHex,
                suscripcionId,
                avisoId,
                timestamp,
                contenido);

            var esperada = Convert.FromHexString(
                firmaEsperada["sha256=".Length..]);

            var recibida = Convert.FromHexString(
                firmaRecibida["sha256=".Length..]);

            return CryptographicOperations.FixedTimeEquals(
                esperada, recibida);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
