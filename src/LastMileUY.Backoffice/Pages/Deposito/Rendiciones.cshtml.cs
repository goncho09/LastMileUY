using LastMileUY.Backoffice.Pages.Configuracion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Deposito;

namespace LastMileUY.Backoffice.Pages.Deposito;

public class RendicionesModel(IDepositoApi api, ILogger<RendicionesModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<RutaPorRendirDto> Rutas { get; private set; } = [];

    protected override async Task CargarAsync(CancellationToken ct) => Rutas = await api.RutasPorRendirAsync(ct);

    public static (string Texto, string Clase) EstadoRendicion(string estado) => estado switch
    {
        EstadosRendicion.Confirmada => ("Rendida", "border-emerald-500/30 text-emerald-300"),
        EstadosRendicion.Declarada => ("El repartidor ya rindió", "border-amber-500/30 text-amber-300"),
        _ => ("Sin rendir", "border-white/10 text-slate-300")
    };
}
