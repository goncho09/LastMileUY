using LastMileUY.Backoffice.Pages.Configuracion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Deposito;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Deposito;

public class IndexModel(IDepositoApi api, ILogger<IndexModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<EnvioPorRecibirDto> PorRecibir { get; private set; } = [];

    public IReadOnlyList<RecepcionDto> Discrepancias { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Codigo { get; set; }

    [BindProperty]
    public string? Resolucion { get; set; }

    protected override async Task CargarAsync(CancellationToken ct)
    {
        PorRecibir = await api.PorRecibirAsync(ct);
        Discrepancias = await api.DiscrepanciasAsync(soloPendientes: true, ct);
    }

    public IActionResult OnPostBuscar()
    {
        if (string.IsNullOrWhiteSpace(Codigo))
            return RedirectToPage();

        return RedirectToPage("Recibir", new { codigo = Codigo.Trim() });
    }

    public async Task<IActionResult> OnPostResolverAsync(int id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Resolucion) || Resolucion.Trim().Length > 500)
        {
            Error = "Contá cómo se resolvió la discrepancia, en hasta 500 caracteres.";
            await CargarConAvisoAsync(ct);
            return Page();
        }

        return await EjecutarAsync(
            () => api.ResolverAsync(id, Resolucion.Trim(), ct),
            "Se marcó la discrepancia como resuelta.",
            null,
            ct);
    }

    public static string Texto(string estado) => estado switch
    {
        EstadosBultoRecibido.Danado => "Dañado",
        EstadosBultoRecibido.Faltante => "Faltante",
        _ => "Recibido"
    };

    public static IEnumerable<string> Detalle(RecepcionDto recepcion) =>
        recepcion.Bultos
            .Where(b => b.Estado != EstadosBultoRecibido.Recibido || b.PesoDistinto)
            .Select(b => b.Estado != EstadosBultoRecibido.Recibido
                ? $"{b.Codigo}: {Texto(b.Estado).ToLowerInvariant()}"
                : $"{b.Codigo}: pesa {Formato.Numero(b.PesoMedidoKg!.Value)} kg (declarado {Formato.Numero(b.PesoDeclaradoKg)} kg)");
}
