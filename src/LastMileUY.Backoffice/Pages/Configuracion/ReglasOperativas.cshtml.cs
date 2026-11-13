using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class ReglasOperativasModel(IConfiguracionApi api, ILogger<ReglasOperativasModel> logger) : PaginaConfiguracion(logger)
{
    public static readonly IReadOnlyList<(string Valor, string Texto)> Modalidades = [("Estandar", "Estándar"), ("Urgente", "Urgente")];

    public IReadOnlyList<ReglaOperativaConfiguracionDto> Reglas { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? Editar { get; set; }

    [BindProperty]
    public FormularioRegla Formulario { get; set; } = new();

    public static string TextoModalidad(string modalidad) =>
        Modalidades.FirstOrDefault(m => m.Valor == modalidad).Texto ?? modalidad;

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Reglas = await api.ListarReglasAsync(ct);

        if (Request.Method == HttpMethods.Get && Editar is int id && Reglas.FirstOrDefault(r => r.Id == id) is { } r)
        {
            Formulario = new FormularioRegla
            {
                Nombre = r.Nombre,
                Descripcion = r.Descripcion,
                Modalidad = r.Modalidad,
                MaximoIntentosEntrega = r.MaximoIntentosEntrega,
                HorasEntreIntentos = r.HorasEntreIntentos,
                PlazoEntregaHoras = r.PlazoEntregaHoras,
                HorasParaIniciarDevolucion = r.HorasParaIniciarDevolucion,
                RequiereFirma = r.RequiereFirma,
                RequiereFotografia = r.RequiereFotografia,
                RequiereDocumentoReceptor = r.RequiereDocumentoReceptor
            };
        }
    }

    public Task<IActionResult> OnPostGuardarAsync(CancellationToken ct) =>
        EjecutarAsync(
            () => Editar is int id
                ? api.EditarReglaAsync(id, Formulario.ARequest(), ct)
                : api.CrearReglaAsync(Formulario.ARequest(), ct),
            Editar is null
                ? $"Se creó la regla \"{Formulario.Nombre.Trim()}\" y pasó a ser la vigente para la modalidad {TextoModalidad(Formulario.Modalidad)}."
                : "Se guardaron los cambios de la regla.",
            new { Editar = (int?)null },
            ct);

    public Task<IActionResult> OnPostCambiarActivaAsync(int id, bool activa, CancellationToken ct)
    {
        ModelState.Clear();
        return EjecutarAsync(
            () => api.CambiarReglaActivaAsync(id, activa, ct),
            activa ? "La regla pasó a ser la vigente para su modalidad." : "La regla quedó inactiva: esa modalidad queda sin regla.",
            new { Editar = (int?)null },
            ct);
    }

    public class FormularioRegla
    {
        [Required(ErrorMessage = "Ingresá el nombre de la regla.")]
        [StringLength(150, ErrorMessage = "El nombre puede tener hasta 150 caracteres.")]
        public string Nombre { get; set; } = "";

        [StringLength(500, ErrorMessage = "La descripción puede tener hasta 500 caracteres.")]
        public string? Descripcion { get; set; }

        [Required(ErrorMessage = "Elegí la modalidad.")]
        public string Modalidad { get; set; } = "Estandar";

        [Range(1, 10, ErrorMessage = "Entre 1 y 10 intentos.")]
        public int MaximoIntentosEntrega { get; set; } = 2;

        [Range(0, 720, ErrorMessage = "Entre 0 y 720 horas.")]
        public int HorasEntreIntentos { get; set; } = 24;

        [Range(1, 720, ErrorMessage = "Entre 1 y 720 horas.")]
        public int PlazoEntregaHoras { get; set; } = 48;

        [Range(0, 720, ErrorMessage = "Entre 0 y 720 horas.")]
        public int HorasParaIniciarDevolucion { get; set; } = 72;

        public bool RequiereFirma { get; set; }

        public bool RequiereFotografia { get; set; }

        public bool RequiereDocumentoReceptor { get; set; }

        public GuardarReglaOperativaRequest ARequest() =>
            new(Nombre.Trim(), Descripcion, Modalidad, MaximoIntentosEntrega, HorasEntreIntentos, PlazoEntregaHoras,
                HorasParaIniciarDevolucion, RequiereFirma, RequiereFotografia, RequiereDocumentoReceptor);
    }
}
