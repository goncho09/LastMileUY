namespace LastMileUY.PublicTracking.Components;

public static class EstadosEnvio
{
    public static readonly string[] Pasos = ["Recibido", "En depósito", "En camino", "Entregado"];

    public static string Titulo(string estado) => estado switch
    {
        "Admitido" => "Recibimos tu envío",
        "EnDeposito" => "Está en el depósito",
        "AsignadoARuta" => "Se prepara para salir",
        "EnTransito" => "Va en camino",
        "Entregado" => "Entregado",
        "EntregadoParcialmente" => "Entregado en parte",
        "NoEntregado" => "No se pudo entregar",
        "Reprogramado" => "Entrega reprogramada",
        "EnDevolucion" => "Vuelve al comercio",
        "Devuelto" => "Se devolvió al comercio",
        "Extraviado" => "Extraviado",
        _ => estado
    };

    public static string Detalle(string estado) => estado switch
    {
        "Admitido" => "El comercio lo despachó y el operador ya lo tiene registrado.",
        "EnDeposito" => "Lo están clasificando para armar el recorrido de entrega.",
        "AsignadoARuta" => "Ya tiene lugar en el recorrido de un repartidor.",
        "EnTransito" => "El repartidor lo lleva hacia la dirección de entrega.",
        "Entregado" => "El envío llegó a destino.",
        "EntregadoParcialmente" => "Llegó una parte de los bultos. El resto todavía no se entregó.",
        "NoEntregado" => "El repartidor pasó pero no pudo entregarlo. Se va a volver a intentar.",
        "Reprogramado" => "Se va a intentar de nuevo en otro recorrido.",
        "EnDevolucion" => "No se pudo entregar y está volviendo al comercio. Consultá con el comercio.",
        "Devuelto" => "El envío volvió al comercio. Consultá con el comercio.",
        "Extraviado" => "El operador no lo encuentra. Consultá con el comercio.",
        _ => ""
    };

    public static int? Paso(string estado) => estado switch
    {
        "Admitido" => 0,
        "EnDeposito" or "AsignadoARuta" or "Reprogramado" => 1,
        "EnTransito" or "NoEntregado" or "EntregadoParcialmente" => 2,
        "Entregado" => 3,
        _ => null
    };

    public static string Tono(string estado) => estado switch
    {
        "Entregado" => "ok",
        "NoEntregado" or "EnDevolucion" or "Devuelto" or "Extraviado" => "alerta",
        _ => "en-curso"
    };

    public static string Hora(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(-3).ToString("dd/MM/yyyy HH:mm");
}
