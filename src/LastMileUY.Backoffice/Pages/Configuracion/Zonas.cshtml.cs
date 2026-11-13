using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class ZonasModel(IConfiguracionApi api, ILogger<ZonasModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<ZonaDto> Zonas { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? Editar { get; set; }

    [BindProperty]
    public FormularioZona Formulario { get; set; } = new();

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Zonas = await api.ListarZonasAsync(ct);

        if (Request.Method == HttpMethods.Get && Editar is int id && Zonas.FirstOrDefault(z => z.Id == id) is { } zona)
            Formulario = new FormularioZona { Nombre = zona.Nombre, Descripcion = zona.Descripcion };
    }

    public Task<IActionResult> OnPostGuardarAsync(CancellationToken ct) =>
        EjecutarAsync(
            () => Editar is int id
                ? api.EditarZonaAsync(id, Formulario.ARequest(), ct)
                : api.CrearZonaAsync(Formulario.ARequest(), ct),
            Editar is null ? $"Se creó la zona \"{Formulario.Nombre.Trim()}\"." : "Se guardaron los cambios de la zona.",
            new { Editar = (int?)null },
            ct);

    public Task<IActionResult> OnPostCambiarActivaAsync(int id, bool activa, CancellationToken ct)
    {
        ModelState.Clear();
        return EjecutarAsync(
            () => api.CambiarZonaActivaAsync(id, activa, ct),
            activa ? "La zona volvió a estar activa." : "La zona quedó inactiva.",
            new { Editar = (int?)null },
            ct);
    }

    public class FormularioZona
    {
        [Required(ErrorMessage = "Ingresá el nombre de la zona.")]
        [StringLength(150, ErrorMessage = "El nombre puede tener hasta 150 caracteres.")]
        public string Nombre { get; set; } = "";

        [StringLength(500, ErrorMessage = "La descripción puede tener hasta 500 caracteres.")]
        public string? Descripcion { get; set; }

        public GuardarZonaRequest ARequest() => new(Nombre.Trim(), Descripcion);
    }
}
