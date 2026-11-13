using Microsoft.JSInterop;

namespace LastMileUY.DriverPWA.Services;

public record Ubicacion(decimal Latitud, decimal Longitud)
{
    public const string SinUbicacion =
        "No se pudo obtener la ubicación del celular. Activá el GPS y dale permiso a la app.";

    public static ValueTask<Ubicacion?> ObtenerAsync(IJSRuntime js) =>
        js.InvokeAsync<Ubicacion?>("lastMileEntrega.ubicacion");
}
