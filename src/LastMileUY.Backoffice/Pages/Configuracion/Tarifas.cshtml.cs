using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class TarifasModel(IConfiguracionApi api, ILogger<TarifasModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<CuadroTarifarioDto> Cuadros { get; private set; } = [];

    [BindProperty]
    public FormularioCuadro Formulario { get; set; } = new();

    public static string ClaseEstado(string estado) => estado switch
    {
        EstadosCuadroTarifario.Vigente => Estilos.Activo,
        EstadosCuadroTarifario.Programado => "rounded-full bg-violet-500/15 px-2 py-0.5 text-xs font-semibold text-violet-300",
        _ => Estilos.Inactivo
    };

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Cuadros = await api.ListarCuadrosAsync(ct);

        if (Request.Method == HttpMethods.Get)
            Formulario.VigenciaDesde ??= DateOnly.FromDateTime(DateTime.Today).AddDays(1);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        CuadroTarifarioDto? creado = null;
        var resultado = await EjecutarAsync(
            async () => creado = await api.CrearCuadroAsync(new CrearCuadroTarifarioRequest(Formulario.VigenciaDesde!.Value, Formulario.CopiarTarifas), ct),
            "Se creó la versión nueva. Cargale las tarifas antes de que empiece a regir.",
            null,
            ct);

        return creado is null ? resultado : RedirectToPage("CuadroTarifario", new { id = creado.Id });
    }

    public class FormularioCuadro
    {
        [Required(ErrorMessage = "Elegí desde qué día rige.")]
        public DateOnly? VigenciaDesde { get; set; }

        public bool CopiarTarifas { get; set; } = true;
    }
}
