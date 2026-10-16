namespace LastMileUY.Backoffice.Servicios;

// Cómo se muestra el resultado de cada operación que llegó de la app del repartidor.
// "Valor" es lo que manda la API (enum ResultadoOperacion). Las clases son las mismas de los estados
// del listado de envíos: el Dockerfile compila Tailwind mirando solo Pages/.
public static class ResultadosSincronizacion
{
    public record Resultado(string Valor, string Texto, string Explicacion, string ClaseCss);

    public static readonly IReadOnlyList<Resultado> Todos =
    [
        new("Aplicada", "Aplicada", "El envío cambió de estado.",
            "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"),
        new("Rechazada", "Rechazada", "No corresponde: faltaba algo o no era de este repartidor.",
            "border-red-500/20 bg-red-500/10 text-red-300"),
        new("Conflicto", "Conflicto", "El envío ya había cambiado en el servidor: no se aplicó y lo tiene que resolver el despachador.",
            "border-amber-500/20 bg-amber-500/10 text-amber-300"),
    ];

    public static string Texto(string valor) =>
        Todos.FirstOrDefault(r => r.Valor == valor)?.Texto ?? valor;

    public static string ClaseCss(string valor) =>
        Todos.FirstOrDefault(r => r.Valor == valor)?.ClaseCss ?? "border-slate-500/20 bg-slate-500/10 text-slate-300";

    public static string TipoOperacion(string tipo) => tipo switch
    {
        "Entrega" => "Entrega",
        "IntentoFallido" => "Intento fallido",
        _ => tipo
    };
}
