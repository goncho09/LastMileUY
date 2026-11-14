using LastMileUY.Backoffice.Pages.Configuracion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Deposito;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Deposito;

public class CargaModel(IDepositoApi api, ILogger<CargaModel> logger) : PaginaConfiguracion(logger)
{
    [BindProperty(SupportsGet = true)]
    public int Ruta { get; set; }

    public CargaRutaDto? Carga { get; private set; }

    [BindProperty]
    public List<string> Cargados { get; set; } = [];

    [BindProperty]
    public string? Observaciones { get; set; }

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Carga = await api.CargaAsync(Ruta, ct);

        if (Carga is null)
            Error ??= "La hoja de ruta no existe o es de otro operador.";
    }

    public Task<IActionResult> OnPostVerificarAsync(CancellationToken ct) =>
        EjecutarAsync(
            async () =>
            {
                var carga = await api.VerificarCargaAsync(Ruta, new VerificarCargaRequest(Cargados, Observaciones), ct);
                var faltan = carga.Bultos.Count(b => b.Cargado != true);

                TempData["Aviso"] = faltan == 0
                    ? "La carga quedó verificada: están todos los bultos."
                    : faltan == 1
                        ? "Falta 1 bulto. Cargalo y volvé a verificar, o bajá su envío de la ruta."
                        : $"Faltan {faltan} bultos. Cargalos y volvé a verificar, o bajá sus envíos de la ruta.";
            },
            "",
            new { Ruta },
            ct);

    public Task<IActionResult> OnPostQuitarAsync(int envioId, string codigo, CancellationToken ct) =>
        EjecutarAsync(
            () => api.QuitarEnvioAsync(Ruta, envioId, ct),
            $"El envío {codigo} se bajó de la ruta y volvió a En depósito.",
            new { Ruta },
            ct);
}
