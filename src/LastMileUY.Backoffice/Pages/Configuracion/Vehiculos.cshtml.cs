using System.ComponentModel.DataAnnotations;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Configuracion;
using Microsoft.AspNetCore.Mvc;

namespace LastMileUY.Backoffice.Pages.Configuracion;

public class VehiculosModel(IConfiguracionApi api, ILogger<VehiculosModel> logger) : PaginaConfiguracion(logger)
{
    public IReadOnlyList<VehiculoConfiguracionDto> Vehiculos { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? Editar { get; set; }

    [BindProperty]
    public FormularioVehiculo Formulario { get; set; } = new();

    protected override async Task CargarAsync(CancellationToken ct)
    {
        Vehiculos = await api.ListarVehiculosAsync(ct);

        if (Request.Method == HttpMethods.Get && Editar is int id && Vehiculos.FirstOrDefault(v => v.Id == id) is { } v)
        {
            Formulario = new FormularioVehiculo
            {
                Matricula = v.Matricula,
                Tipo = v.Tipo,
                CapacidadPesoKg = v.CapacidadPesoKg,
                CapacidadVolumenM3 = v.CapacidadVolumenM3
            };
        }
    }

    public Task<IActionResult> OnPostGuardarAsync(CancellationToken ct) =>
        EjecutarAsync(
            () => Editar is int id
                ? api.EditarVehiculoAsync(id, Formulario.ARequest(), ct)
                : api.CrearVehiculoAsync(Formulario.ARequest(), ct),
            Editar is null ? $"Se agregó el vehículo {Formulario.Matricula.Trim().ToUpperInvariant()}." : "Se guardaron los cambios del vehículo.",
            new { Editar = (int?)null },
            ct);

    public Task<IActionResult> OnPostCambiarActivoAsync(int id, bool activo, CancellationToken ct)
    {
        ModelState.Clear();
        return EjecutarAsync(
            () => api.CambiarVehiculoActivoAsync(id, activo, ct),
            activo ? "El vehículo volvió a estar disponible." : "El vehículo quedó dado de baja: ya no se ofrece al armar rutas.",
            new { Editar = (int?)null },
            ct);
    }

    public class FormularioVehiculo
    {
        [Required(ErrorMessage = "Ingresá la matrícula.")]
        [StringLength(20, ErrorMessage = "La matrícula puede tener hasta 20 caracteres.")]
        public string Matricula { get; set; } = "";

        [Required(ErrorMessage = "Ingresá el tipo de vehículo.")]
        [StringLength(50, ErrorMessage = "El tipo puede tener hasta 50 caracteres.")]
        public string Tipo { get; set; } = "";

        [Required(ErrorMessage = "Ingresá la capacidad de peso.")]
        [Range(typeof(decimal), "0.01", "99999999", ErrorMessage = "La capacidad de peso tiene que ser mayor que cero.")]
        public decimal? CapacidadPesoKg { get; set; }

        [Required(ErrorMessage = "Ingresá la capacidad de volumen.")]
        [Range(typeof(decimal), "0.001", "9999999", ErrorMessage = "La capacidad de volumen tiene que ser mayor que cero.")]
        public decimal? CapacidadVolumenM3 { get; set; }

        public GuardarVehiculoRequest ARequest() =>
            new(Matricula.Trim(), Tipo.Trim(), CapacidadPesoKg!.Value, CapacidadVolumenM3!.Value);
    }
}
