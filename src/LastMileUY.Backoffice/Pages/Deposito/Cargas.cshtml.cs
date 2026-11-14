using LastMileUY.Backoffice.Pages.Configuracion;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Deposito;

namespace LastMileUY.Backoffice.Pages.Deposito;

public class CargasModel(IDepositoApi api, ILogger<CargasModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<RutaPorCargarDto> Rutas { get; private set; } = [];

    protected override async Task CargarAsync(CancellationToken ct) => Rutas = await api.RutasPorCargarAsync(ct);

    public static (string Texto, string Clase) EstadoCarga(string estado) => estado switch
    {
        EstadosCarga.Verificada => ("Carga verificada", "border-emerald-500/30 text-emerald-300"),
        EstadosCarga.Incompleta => ("Faltan bultos", "border-red-500/30 text-red-300"),
        _ => ("Sin verificar", "border-white/10 text-slate-300")
    };
}
