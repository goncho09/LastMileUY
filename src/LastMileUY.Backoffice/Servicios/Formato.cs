using System.Globalization;

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
}
