using System.Text.Json;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Flota;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Rutas;

public class FlotaModel(IFlotaApi api, ILogger<FlotaModel> logger) : PageModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FlotaDto? Flota { get; private set; }

    public string? Error { get; private set; }

    public string Marcadores => JsonSerializer.Serialize(
        (Flota?.Rutas ?? [])
            .Where(r => r.Latitud is not null && r.Longitud is not null)
            .Select(r => new
            {
                r.RutaId,
                r.Ruta,
                r.Repartidor,
                r.Vehiculo,
                Latitud = r.Latitud!.Value,
                Longitud = r.Longitud!.Value,
                Hace = Hace(r.UltimaPosicion)
            }),
        Json);

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Flota = await api.ObtenerAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "No se pudo cargar la flota");
            Error = "No se pudo cargar la flota. Se vuelve a intentar sola en unos segundos.";
        }

        Response.Headers.CacheControl = "no-store";
    }

    public string Hace(DateTime? momento)
    {
        if (momento is not { } valor)
            return "sin posición todavía";

        var minutos = (int)Math.Max(0, ((Flota?.Generado ?? DateTime.UtcNow) - valor).TotalMinutes);

        return minutos switch
        {
            0 => "hace menos de un minuto",
            1 => "hace 1 minuto",
            < 60 => $"hace {minutos} minutos",
            _ => $"hace {minutos / 60} h {minutos % 60} min"
        };
    }

    public static bool Desactualizada(VehiculoEnRutaDto ruta, DateTime generado) =>
        ruta.UltimaPosicion is null || generado - ruta.UltimaPosicion.Value > TimeSpan.FromMinutes(15);
}
