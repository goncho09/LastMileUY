using LastMileUY.Backoffice.Pages.Configuracion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Deposito;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Deposito;

public class RecibirModel(IDepositoApi api, ILogger<RecibirModel> logger) : PaginaConfiguracion(logger)
{
    [BindProperty(SupportsGet = true)]
    public string Codigo { get; set; } = "";

    public EnvioParaRecibirDto? Envio { get; private set; }

    [BindProperty]
    public List<FilaBulto> Bultos { get; set; } = [];

    [BindProperty]
    public string? Observaciones { get; set; }

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Envio = string.IsNullOrWhiteSpace(Codigo) ? null : await api.BuscarAsync(Codigo, ct);

        if (Envio is null)
        {
            Error ??= $"No hay ningún envío ni bulto con el código \"{Codigo}\".";
            return;
        }

        if (Bultos.Count == 0)
        {
            var escaneado = Envio.Bultos.Any(b => string.Equals(b.Codigo, Codigo.Trim(), StringComparison.OrdinalIgnoreCase));

            Bultos = Envio.Bultos
                .Select(b => new FilaBulto
                {
                    Codigo = b.Codigo,
                    Estado = EstadosBultoRecibido.Recibido,
                    Escaneado = escaneado && string.Equals(b.Codigo, Codigo.Trim(), StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await CargarConAvisoAsync(ct);

        if (Envio is null)
            return Page();

        try
        {
            var recepcion = await api.RecibirAsync(new RegistrarRecepcionRequest(
                Envio.Id,
                Bultos.Select(b => new BultoRecibidoRequest(b.Codigo, b.Estado, b.PesoMedidoKg)).ToList(),
                Observaciones), ct);

            TempData["Aviso"] = recepcion switch
            {
                { Aceptada: false } => $"El envío {Envio.CodigoSeguimiento} no ingresó porque faltan bultos. Quedó como discrepancia.",
                { TieneDiscrepancias: true } => $"El envío {Envio.CodigoSeguimiento} ingresó al depósito con observaciones.",
                _ => $"El envío {Envio.CodigoSeguimiento} ingresó al depósito."
            };

            return RedirectToPage("Index");
        }
        catch (OperacionRechazadaException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "No se pudo registrar la recepción de {Codigo}", Envio.CodigoSeguimiento);
            Error = "No se pudo conectar con el servidor. Probá de nuevo en unos minutos.";
        }

        return Page();
    }

    public BultoPorRecibirDto? Declarado(string codigo) => Envio?.Bultos.FirstOrDefault(b => b.Codigo == codigo);

    public class FilaBulto
    {
        public string Codigo { get; set; } = "";

        public string Estado { get; set; } = EstadosBultoRecibido.Recibido;

        public decimal? PesoMedidoKg { get; set; }

        public bool Escaneado { get; set; }
    }
}
