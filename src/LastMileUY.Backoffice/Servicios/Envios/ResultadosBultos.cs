namespace LastMileUY.Backoffice.Servicios;

public static class ResultadosBultos
{
    public static string TextoEstado(string estado) => estado switch
    {
        "Pendiente" => "Pendiente",
        "Entregado" => "Entregado",
        "NoEntregado" => "No entregado",
        "Devuelto" => "Devuelto",
        "Extraviado" => "Extraviado",
        _ => estado
    };

    public static string ClaseEstado(string estado) => estado switch
    {
        "Entregado" => "border-emerald-500/25 bg-emerald-500/10 text-emerald-300",
        "NoEntregado" => "border-amber-500/25 bg-amber-500/10 text-amber-300",
        "Devuelto" => "border-slate-500/25 bg-slate-500/10 text-slate-300",
        "Extraviado" => "border-red-500/25 bg-red-500/10 text-red-300",
        _ => "border-blue-500/25 bg-blue-500/10 text-blue-300"
    };

    public static string TextoIntento(string resultado) => resultado switch
    {
        "Entregado" => "Entregado",
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
}
