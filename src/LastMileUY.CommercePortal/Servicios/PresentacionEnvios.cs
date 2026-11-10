namespace LastMileUY.CommercePortal.Servicios;

// Cómo se muestran los envíos en el portal: el texto y el color de cada estado (del envío, de cada
// bulto y de cada intento) y las fechas en hora de Uruguay. Lo usan el listado y el detalle.
public static class PresentacionEnvios
{
    // Los estados del envío (los nombres son los que devuelve la API) con su texto para mostrar
    public static readonly (string Valor, string Texto)[] Estados =
    [
        ("Admitido", "Admitido"),
        ("EnDeposito", "En depósito"),
        ("AsignadoARuta", "Asignado a ruta"),
        ("EnTransito", "En tránsito"),
        ("Entregado", "Entregado"),
        ("EntregadoParcialmente", "Entregado parcialmente"),
        ("NoEntregado", "No entregado"),
        ("Reprogramado", "Reprogramado"),
        ("EnDevolucion", "En devolución"),
        ("Devuelto", "Devuelto"),
        ("Extraviado", "Extraviado")
    ];

    public static string TextoEstado(string estado) =>
        Estados.FirstOrDefault(e => e.Valor == estado).Texto ?? estado;

    public static string ClaseEstado(string estado) => estado switch
    {
        "Entregado" => "border-emerald-500/25 bg-emerald-500/10 text-emerald-300",
        "EnTransito" or "AsignadoARuta" => "border-blue-500/25 bg-blue-500/10 text-blue-300",
        "NoEntregado" or "Extraviado" => "border-red-500/25 bg-red-500/10 text-red-300",
        "EntregadoParcialmente" or "Reprogramado" or "EnDevolucion" or "Devuelto" => "border-amber-500/25 bg-amber-500/10 text-amber-300",
        _ => "border-white/15 bg-white/5 text-slate-300"
    };

    // Estado de un bulto: Pendiente, Entregado, NoEntregado, Devuelto o Extraviado
    public static string TextoEstadoBulto(string estado) => estado switch
    {
        "NoEntregado" => "No entregado",
        _ => estado
    };

    public static string ClaseEstadoBulto(string estado) => estado switch
    {
        "Entregado" => "border-emerald-500/25 bg-emerald-500/10 text-emerald-300",
        "NoEntregado" => "border-amber-500/25 bg-amber-500/10 text-amber-300",
        "Extraviado" => "border-red-500/25 bg-red-500/10 text-red-300",
        "Devuelto" => "border-white/15 bg-white/5 text-slate-300",
        _ => "border-blue-500/25 bg-blue-500/10 text-blue-300"
    };

    // Resultado de un intento: Entregado, Parcial o NoEntregado
    public static string TextoIntento(string resultado) => resultado switch
    {
        "Parcial" => "Entrega parcial",
        "NoEntregado" => "No entregado",
        _ => resultado
    };

    public static string ClaseIntento(string resultado) => resultado switch
    {
        "Entregado" => "border-emerald-500/25 bg-emerald-500/10 text-emerald-300",
        "Parcial" => "border-amber-500/25 bg-amber-500/10 text-amber-300",
        _ => "border-red-500/25 bg-red-500/10 text-red-300"
    };

    // La API guarda las fechas en UTC; se muestran en hora de Uruguay
    private static readonly TimeZoneInfo? ZonaUruguay = BuscarZona();

    private static TimeZoneInfo? BuscarZona()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo"); }
        catch (Exception) { return null; }
    }

    public static string Fecha(DateTime fecha)
    {
        var utc = fecha.Kind == DateTimeKind.Local ? fecha.ToUniversalTime() : DateTime.SpecifyKind(fecha, DateTimeKind.Utc);
        var local = ZonaUruguay is null ? utc : TimeZoneInfo.ConvertTimeFromUtc(utc, ZonaUruguay);
        return local.ToString("dd/MM/yyyy HH:mm");
    }
}
