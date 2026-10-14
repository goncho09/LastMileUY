using System.Globalization;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.Backoffice.Servicios;

// Formatos de pantalla. Las fechas llegan en UTC y se muestran en hora de Uruguay.
public static class Formato
{
    private static readonly TimeZoneInfo HoraUruguay =
        TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-UY");

    public static string Fecha(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), HoraUruguay)
            .ToString("dd/MM/yyyy HH:mm", Cultura);

    public static string Fecha(DateTime? utc) => utc is null ? "—" : Fecha(utc.Value);

    public static string Pesos(decimal? monto) =>
        monto is null ? "—" : "$ " + monto.Value.ToString("N2", Cultura);

    public static string Numero(decimal valor) => valor.ToString("0.##", Cultura);

    // "Firma, foto y documento del receptor" / "Nada" según lo que pida la regla al entregar
    public static string Requisitos(ReglaAplicadaDto regla)
    {
        var requisitos = new List<string>();

        if (regla.RequiereFirma) requisitos.Add("firma");
        if (regla.RequiereFotografia) requisitos.Add("foto");
        if (regla.RequiereDocumentoReceptor) requisitos.Add("documento del receptor");

        if (requisitos.Count == 0)
        {
            return "Nada";
        }

        var texto = requisitos.Count == 1
            ? requisitos[0]
            : string.Join(", ", requisitos.SkipLast(1)) + " y " + requisitos[^1];

        return char.ToUpper(texto[0], Cultura) + texto[1..];
    }
}
