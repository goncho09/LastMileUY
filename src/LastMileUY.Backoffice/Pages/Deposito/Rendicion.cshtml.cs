using LastMileUY.Backoffice.Pages.Configuracion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Deposito;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Deposito;

public class RendicionModel(IDepositoApi api, ILogger<RendicionModel> logger) : PaginaConfiguracion(logger)
{
    [BindProperty(SupportsGet = true)]
    public int Ruta { get; set; }

    public RendicionRutaDto? Rendicion { get; private set; }

    [BindProperty]
    public List<string> Recibidos { get; set; } = [];

    [BindProperty]
    public string? Observaciones { get; set; }

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Rendicion = await api.RendicionAsync(Ruta, ct);

        if (Rendicion is null)
            Error ??= "La hoja de ruta no existe o es de otro operador.";
    }

    public Task<IActionResult> OnPostConfirmarAsync(CancellationToken ct) =>
        EjecutarAsync(
            async () =>
            {
                var rendicion = await api.ConfirmarRendicionAsync(Ruta, new ConfirmarRendicionRequest(Recibidos, Observaciones), ct);

                TempData["Aviso"] = rendicion.Faltantes.Count == 0
                    ? "Rendición confirmada: volvieron todos los bultos y la ruta quedó cerrada."
                    : $"Rendición confirmada con {rendicion.Faltantes.Count} bulto(s) faltante(s). Sus envíos quedan como estaban para investigarlos.";
            },
            "",
            new { Ruta },
            ct);

    public static string Destino(string estadoEnvio) => estadoEnvio switch
    {
        "EnTransito" => "Sin intento: vuelve a En depósito",
        "NoEntregado" => "Se reprograma, o vuelve al comercio si no quedan intentos",
        "EntregadoParcialmente" => "El resto se reprograma, o vuelve al comercio",
        "Reprogramado" => "Ya está reprogramado",
        "EnDevolucion" => "Ya está en devolución al comercio",
        _ => estadoEnvio
    };
}
