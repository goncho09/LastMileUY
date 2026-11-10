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

    // Volumen en m³: hasta 3 decimales (un paquete chico ocupa 0,001 m³)
    public static string Volumen(decimal m3) => m3.ToString("0.###", Cultura) + " m³";

    public static string Fecha(DateOnly fecha) => fecha.ToString("dd/MM/yyyy", Cultura);

    public static string Hora(TimeOnly hora) => hora.ToString("HH:mm", Cultura);

    // El día de hoy en Uruguay (para el valor por defecto de las fechas de las rutas)
    public static DateOnly Hoy() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HoraUruguay));

    // "Firma, foto y documento del receptor" / "Nada" según lo que pida la regla al entregar
    public static string Requisitos(ReglaAplicadaDto regla) =>
        Requisitos(regla.RequiereFirma, regla.RequiereFotografia, regla.RequiereDocumentoReceptor);

    public static string Requisitos(bool firma, bool foto, bool documento)
    {
        var requisitos = new List<string>();

        if (firma) requisitos.Add("firma");
        if (foto) requisitos.Add("foto");
        if (documento) requisitos.Add("documento del receptor");

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
