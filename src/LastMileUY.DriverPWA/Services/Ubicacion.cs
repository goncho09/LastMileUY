using Microsoft.JSInterop;

namespace LastMileUY.DriverPWA.Services;

// Dónde estaba el celular al registrar una entrega o un intento (la API la guarda en el historial)
public record Ubicacion(decimal Latitud, decimal Longitud)
{
    public const string SinUbicacion =
        "No se pudo obtener la ubicación del celular. Activá el GPS y dale permiso a la app.";

    // null si el celular no la da (wwwroot/js/entrega.js)
    public static ValueTask<Ubicacion?> ObtenerAsync(IJSRuntime js) =>
        js.InvokeAsync<Ubicacion?>("lastMileEntrega.ubicacion");
}
