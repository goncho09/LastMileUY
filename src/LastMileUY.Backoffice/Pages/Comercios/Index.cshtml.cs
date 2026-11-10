using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Comercios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Comercios;

[Authorize(Policy = Permisos.PoliticaAdministracionGlobal)]
public sealed class IndexModel(IGestionComerciosApi api) : PageModel
{
    public List<ComercioAdminDto> Comercios { get; private set; } = [];
    public string? ErrorCarga { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            Comercios = await api.ListarAsync();
        }
        catch (HttpRequestException)
        {
            ErrorCarga = "No se pudo cargar el listado de comercios.";
        }
    }
}