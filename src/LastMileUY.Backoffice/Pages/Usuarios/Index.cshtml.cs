using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Usuarios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LastMileUY.Backoffice.Pages.Usuarios;

public sealed class IndexModel(
    IUsuariosApi usuariosApi,
    ILogger<IndexModel> logger) : PageModel
{
    public IReadOnlyList<UsuarioDto> Usuarios { get; private set; } = [];

    [BindProperty]
    public NuevoUsuarioInput Nuevo { get; set; } = new();

    [TempData]
    public string? Aviso { get; set; }

    [TempData]
    public string? ErrorOperacion { get; set; }

    public string? ErrorCarga { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Usuarios = await usuariosApi.ListarAsync(ct);
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
            logger.LogError(ex, "Error al listar usuarios");
            ErrorCarga = "No se pudo cargar el personal.";
        }
    }

    public async Task<IActionResult> OnPostCrearAsync(
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Nuevo.Email) ||
            string.IsNullOrWhiteSpace(Nuevo.Nombre) ||
            string.IsNullOrWhiteSpace(Nuevo.Apellido) ||
            string.IsNullOrWhiteSpace(Nuevo.Password) ||
            !new[] { "Despachador", "OperarioDeposito", "Repartidor" }
                .Contains(Nuevo.Rol))
        {
            ErrorOperacion = "Completá todos los campos y seleccioná un rol válido.";
            return RedirectToPage();
        }

        try
        {
            var request = new CrearUsuarioRequest(
                Nuevo.Email.Trim(),
                Nuevo.Nombre.Trim(),
                Nuevo.Apellido.Trim(),
                Nuevo.Password,
                Nuevo.Rol,
                null,
                null);

            await usuariosApi.CrearAsync(request, ct);

            Aviso = "Usuario creado correctamente.";
        }
        catch (SesionVencidaException)
        {
            throw;
        }
        catch (OperacionRechazadaException ex)
        {
            ErrorOperacion = ex.Message;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Error al crear usuario");
            ErrorOperacion = "No se pudo conectar con la API.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCambiarEstadoAsync(
        string usuarioId,
        bool activo,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(usuarioId))
        {
            ErrorOperacion = "Usuario inválido.";
            return RedirectToPage();
        }

        try
        {
            await usuariosApi.CambiarEstadoAsync(
                usuarioId, activo, ct);

            Aviso = activo
                ? "Usuario activado correctamente."
                : "Usuario desactivado correctamente.";
        }
        catch (SesionVencidaException)
        {
            throw;
        }
        catch (OperacionRechazadaException ex)
        {
            ErrorOperacion = ex.Message;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Error al cambiar estado de usuario");
            ErrorOperacion = "No se pudo conectar con la API.";
        }

        return RedirectToPage();
    }

    public sealed class NuevoUsuarioInput
    {
        public string Email { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Apellido { get; set; } = "";
        public string Password { get; set; } = "";
        public string Rol { get; set; } = "Despachador";
    }
}
