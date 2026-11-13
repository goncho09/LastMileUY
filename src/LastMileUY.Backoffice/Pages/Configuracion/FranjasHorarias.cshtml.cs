using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class FranjasHorariasModel(IConfiguracionApi api, ILogger<FranjasHorariasModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<ZonaDto> Zonas { get; private set; } = [];

    public ZonaDto? ZonaElegida { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? Zona { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Editar { get; set; }

    [BindProperty]
    public FormularioFranja Formulario { get; set; } = new();

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Zonas = await api.ListarZonasAsync(ct);
        ZonaElegida = Zonas.FirstOrDefault(z => z.Id == Zona) ?? Zonas.FirstOrDefault(z => z.Activa) ?? Zonas.FirstOrDefault();
        Zona = ZonaElegida?.Id;

        if (Request.Method == HttpMethods.Get && Editar is int id && ZonaElegida?.Franjas.FirstOrDefault(f => f.Id == id) is { } franja)
            Formulario = new FormularioFranja { Nombre = franja.Nombre, HoraInicio = franja.HoraInicio, HoraFin = franja.HoraFin };
    }

    public Task<IActionResult> OnPostGuardarAsync(CancellationToken ct)
    {
        if (Formulario.HoraInicio is { } inicio && Formulario.HoraFin is { } fin && fin <= inicio)
            ModelState.AddModelError("Formulario.HoraFin", "La hora de fin tiene que ser posterior a la de inicio.");

        if (Zona is not int zonaId)
        {
            Error = "Elegí una zona.";
            ModelState.AddModelError("", Error);
            zonaId = 0;
        }

        return EjecutarAsync(
            () => Editar is int id
                ? api.EditarFranjaAsync(zonaId, id, Formulario.ARequest(), ct)
                : api.CrearFranjaAsync(zonaId, Formulario.ARequest(), ct),
            Editar is null ? $"Se creó la franja \"{Formulario.Nombre.Trim()}\"." : "Se guardaron los cambios de la franja.",
            new { Zona, Editar = (int?)null },
            ct);
    }

    public Task<IActionResult> OnPostCambiarActivaAsync(int id, bool activa, CancellationToken ct)
    {
        ModelState.Clear();
        return EjecutarAsync(
            () => api.CambiarFranjaActivaAsync(Zona ?? 0, id, activa, ct),
            activa ? "La franja volvió a estar activa." : "La franja quedó inactiva.",
            new { Zona, Editar = (int?)null },
            ct);
    }

    public class FormularioFranja
    {
        [Required(ErrorMessage = "Ingresá el nombre de la franja.")]
        [StringLength(100, ErrorMessage = "El nombre puede tener hasta 100 caracteres.")]
        public string Nombre { get; set; } = "";

        [Required(ErrorMessage = "Ingresá la hora de inicio.")]
        public TimeOnly? HoraInicio { get; set; }

        [Required(ErrorMessage = "Ingresá la hora de fin.")]
        public TimeOnly? HoraFin { get; set; }

        public GuardarFranjaHorariaRequest ARequest() => new(Nombre.Trim(), HoraInicio!.Value, HoraFin!.Value);
    }
}
