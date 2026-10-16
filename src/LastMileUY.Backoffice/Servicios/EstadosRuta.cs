namespace LastMileUY.Backoffice.Servicios;

// Cómo se muestra cada estado de la hoja de ruta. "Valor" es el nombre que manda la API (enum EstadoRuta).
// Ojo con las clases: el Dockerfile compila Tailwind mirando solo Pages/, así que tienen que
// aparecer también en alguna página (son las mismas de los estados del listado de envíos).
public static class EstadosRuta
{
    public const string Planificada = "Planificada";

    public record Estado(string Valor, string Texto, string ClaseCss);

    public static readonly IReadOnlyList<Estado> Todos =
    [
        new(Planificada, "Planificada", "border-amber-500/20 bg-amber-500/10 text-amber-300"),
        new("Despachada", "Despachada", "border-blue-500/20 bg-blue-500/10 text-blue-300"),
        new("EnCurso", "En curso", "border-violet-500/20 bg-violet-500/10 text-violet-300"),
        new("Finalizada", "Finalizada", "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"),
        new("Cancelada", "Cancelada", "border-red-500/20 bg-red-500/10 text-red-300"),
    ];

    public static string Texto(string valor) =>
        Todos.FirstOrDefault(e => e.Valor == valor)?.Texto ?? valor;

    public static string ClaseCss(string valor) =>
        Todos.FirstOrDefault(e => e.Valor == valor)?.ClaseCss ?? "border-slate-500/20 bg-slate-500/10 text-slate-300";
}
