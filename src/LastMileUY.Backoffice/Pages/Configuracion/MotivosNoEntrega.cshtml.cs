using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class MotivosNoEntregaModel(IConfiguracionApi api, ILogger<MotivosNoEntregaModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<MotivoNoEntregaConfiguracionDto> Motivos { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? Editar { get; set; }

    [BindProperty]
    public FormularioMotivo Formulario { get; set; } = new();

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Motivos = await api.ListarMotivosAsync(ct);

        if (Request.Method == HttpMethods.Get && Editar is int id && Motivos.FirstOrDefault(m => m.Id == id) is { } motivo)
            Formulario = new FormularioMotivo { Nombre = motivo.Nombre, Descripcion = motivo.Descripcion };
    }

    public Task<IActionResult> OnPostGuardarAsync(CancellationToken ct) =>
        EjecutarAsync(
            () => Editar is int id
                ? api.EditarMotivoAsync(id, Formulario.ARequest(), ct)
                : api.CrearMotivoAsync(Formulario.ARequest(), ct),
            Editar is null ? $"Se agregó el motivo \"{Formulario.Nombre.Trim()}\"." : "Se guardaron los cambios del motivo.",
            new { Editar = (int?)null },
            ct);

    public Task<IActionResult> OnPostCambiarActivoAsync(int id, bool activo, CancellationToken ct)
    {
        ModelState.Clear();
        return EjecutarAsync(
            () => api.CambiarMotivoActivoAsync(id, activo, ct),
            activo ? "El motivo volvió a aparecer en la app del repartidor." : "El motivo ya no aparece en la app del repartidor.",
            new { Editar = (int?)null },
            ct);
    }

    public class FormularioMotivo
    {
        [Required(ErrorMessage = "Ingresá el motivo.")]
        [StringLength(150, ErrorMessage = "El motivo puede tener hasta 150 caracteres.")]
        public string Nombre { get; set; } = "";

        [StringLength(500, ErrorMessage = "La descripción puede tener hasta 500 caracteres.")]
        public string? Descripcion { get; set; }

        public GuardarMotivoNoEntregaRequest ARequest() => new(Nombre.Trim(), Descripcion);
    }
}
