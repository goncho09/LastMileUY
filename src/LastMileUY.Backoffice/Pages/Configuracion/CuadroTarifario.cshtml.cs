using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class CuadroTarifarioModel(IConfiguracionApi api, ILogger<CuadroTarifarioModel> logger) : PaginaConfiguracion(logger)
{
    public CuadroTarifarioDto? Cuadro { get; private set; }

    public IReadOnlyList<ZonaDto> Zonas { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Editar { get; set; }

    [BindProperty]
    public FormularioTarifa Formulario { get; set; } = new();

    public override async Task OnGetAsync(CancellationToken ct)
    {
        await base.OnGetAsync(ct);

        if (Cuadro is null && Error is null)
            Response.StatusCode = StatusCodes.Status404NotFound;
    }

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Cuadro = await api.ObtenerCuadroAsync(Id, ct);
        Zonas = await api.ListarZonasAsync(ct);

        if (Request.Method == HttpMethods.Get && Editar is int id && Cuadro?.Tarifas.FirstOrDefault(t => t.Id == id) is { } t)
        {
            Formulario = new FormularioTarifa
            {
                Nombre = t.Nombre,
                ZonaId = t.ZonaId,
                Modalidad = t.Modalidad,
                PesoMinimoKg = t.PesoMinimoKg,
                PesoMaximoKg = t.PesoMaximoKg,
                VolumenMinimoM3 = t.VolumenMinimoM3,
                VolumenMaximoM3 = t.VolumenMaximoM3,
                PrecioBase = t.PrecioBase,
                PrecioPorKgAdicional = t.PrecioPorKgAdicional
            };
        }
    }

    public Task<IActionResult> OnPostGuardarAsync(CancellationToken ct)
    {
        if (Formulario.PesoMinimoKg >= Formulario.PesoMaximoKg)
            ModelState.AddModelError("Formulario.PesoMaximoKg", "Tiene que ser mayor que el peso mínimo.");

        if (Formulario.VolumenMinimoM3 >= Formulario.VolumenMaximoM3)
            ModelState.AddModelError("Formulario.VolumenMaximoM3", "Tiene que ser mayor que el volumen mínimo.");

        return EjecutarAsync(
            () => Editar is int tarifaId
                ? api.EditarTarifaAsync(Id, tarifaId, Formulario.ARequest(), ct)
                : api.AgregarTarifaAsync(Id, Formulario.ARequest(), ct),
            Editar is null ? $"Se agregó la tarifa \"{Formulario.Nombre.Trim()}\"." : "Se guardaron los cambios de la tarifa.",
            new { Id, Editar = (int?)null },
            ct);
    }

    public Task<IActionResult> OnPostCambiarActivaAsync(int tarifaId, bool activa, CancellationToken ct)
    {
        ModelState.Clear();
        return EjecutarAsync(
            () => api.CambiarTarifaActivaAsync(Id, tarifaId, activa, ct),
            activa ? "La tarifa volvió a estar activa." : "La tarifa quedó inactiva.",
            new { Id, Editar = (int?)null },
            ct);
    }

    public class FormularioTarifa
    {
        [Required(ErrorMessage = "Ingresá un nombre para la tarifa.")]
        [StringLength(150, ErrorMessage = "El nombre puede tener hasta 150 caracteres.")]
        public string Nombre { get; set; } = "";

        [Required(ErrorMessage = "Elegí la zona.")]
        public int? ZonaId { get; set; }

        [Required(ErrorMessage = "Elegí la modalidad.")]
        public string Modalidad { get; set; } = "Estandar";

        [Range(typeof(decimal), "0", "99999", ErrorMessage = "Entre 0 y 99999 kg.")]
        public decimal PesoMinimoKg { get; set; }

        [Range(typeof(decimal), "0.01", "99999", ErrorMessage = "Entre 0,01 y 99999 kg.")]
        public decimal PesoMaximoKg { get; set; } = 5;

        [Range(typeof(decimal), "0", "999", ErrorMessage = "Entre 0 y 999 m³.")]
        public decimal VolumenMinimoM3 { get; set; }

        [Range(typeof(decimal), "0.001", "999", ErrorMessage = "Entre 0,001 y 999 m³.")]
        public decimal VolumenMaximoM3 { get; set; } = 0.5m;

        [Range(typeof(decimal), "0", "9999999", ErrorMessage = "El precio no puede ser negativo.")]
        public decimal PrecioBase { get; set; }

        [Range(typeof(decimal), "0", "9999999", ErrorMessage = "El precio no puede ser negativo.")]
        public decimal PrecioPorKgAdicional { get; set; }

        public GuardarTarifaRequest ARequest() =>
            new(Nombre.Trim(), ZonaId ?? 0, Modalidad, PesoMinimoKg, PesoMaximoKg, VolumenMinimoM3, VolumenMaximoM3,
                PrecioBase, PrecioPorKgAdicional);
    }
}
