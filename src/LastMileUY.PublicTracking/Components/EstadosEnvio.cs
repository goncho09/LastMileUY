namespace LastMileUY.PublicTracking.Components;

// Cómo se le cuenta cada estado del envío (los de EstadoEnvio en la API) a quien lo espera
public static class EstadosEnvio
{
    // Los pasos de un envío que sale bien; la barra de avance marca hasta dónde llegó
    public static readonly string[] Pasos = ["Recibido", "En depósito", "En camino", "Entregado"];

    public static string Titulo(string estado) => estado switch
    {
        "Admitido" => "Recibimos tu envío",
        "EnDeposito" => "Está en el depósito",
        "AsignadoARuta" => "Se prepara para salir",
        "EnTransito" => "Va en camino",
        "Entregado" => "Entregado",
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
        "NoEntregado" => "El repartidor pasó pero no pudo entregarlo. Se va a volver a intentar.",
        "Reprogramado" => "Se va a intentar de nuevo en otro recorrido.",
        "EnDevolucion" => "No se pudo entregar y está volviendo al comercio. Consultá con el comercio.",
        "Devuelto" => "El envío volvió al comercio. Consultá con el comercio.",
        "Extraviado" => "El operador no lo encuentra. Consultá con el comercio.",
        _ => ""
    };

    // Índice en Pasos, o null si el envío salió del camino normal (devolución, extravío)
    public static int? Paso(string estado) => estado switch
    {
        "Admitido" => 0,
        "EnDeposito" or "AsignadoARuta" or "Reprogramado" => 1,
        "EnTransito" or "NoEntregado" => 2,
        "Entregado" => 3,
        _ => null
    };

    // Para el color de la tarjeta: "ok", "alerta" o "en-curso"
    public static string Tono(string estado) => estado switch
    {
        "Entregado" => "ok",
        "NoEntregado" or "EnDevolucion" or "Devuelto" or "Extraviado" => "alerta",
        _ => "en-curso"
    };

    // Uruguay no tiene horario de verano desde 2015: siempre UTC-3. La API manda las horas en UTC.
    public static string Hora(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(-3).ToString("dd/MM/yyyy HH:mm");
}
