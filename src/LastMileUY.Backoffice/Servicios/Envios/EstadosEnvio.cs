namespace LastMileUY.Backoffice.Servicios;

public static class EstadosEnvio
{
    public record Estado(string Valor, string Texto, string ClaseCss);

    public static readonly IReadOnlyList<Estado> Todos =
    [
        new("Admitido", "Admitido", "text-bg-secondary"),
        new("EnDeposito", "En depósito", "text-bg-info"),
        new("AsignadoARuta", "Asignado a ruta", "text-bg-primary"),
        new("EnTransito", "En tránsito", "text-bg-primary"),
        new("Entregado", "Entregado", "text-bg-success"),
        new("EntregadoParcialmente", "Entregado parcialmente", "text-bg-warning"),
        new("NoEntregado", "No entregado", "text-bg-warning"),
        new("Reprogramado", "Reprogramado", "text-bg-warning"),
        new("EnDevolucion", "En devolución", "text-bg-dark"),
        new("Devuelto", "Devuelto", "text-bg-dark"),
        new("Extraviado", "Extraviado", "text-bg-danger"),
    ];

    public static string Texto(string valor) =>
        Todos.FirstOrDefault(e => e.Valor == valor)?.Texto ?? valor;

    public static string ClaseCss(string valor) =>
        Todos.FirstOrDefault(e => e.Valor == valor)?.ClaseCss ?? "text-bg-light";
}
