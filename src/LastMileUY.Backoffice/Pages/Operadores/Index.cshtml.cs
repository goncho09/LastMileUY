using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Operadores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Operadores;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class IndexModel(
    IGestionOperadoresApi operadoresApi,
    ILogger<IndexModel> logger) : PageModel
{
    public List<OperadorAdminDto> Operadores { get; private set; } = [];

    public string? ErrorCarga { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            Operadores = await operadoresApi.ListarAsync();
        }
        catch (SesionVencidaException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or
                TaskCanceledException or
                InvalidOperationException or
                OperacionRechazadaException)
        {
            logger.LogError(ex, "Error al listar operadores");
            ErrorCarga = "No se pudo cargar el listado de operadores.";
        }
    }
}