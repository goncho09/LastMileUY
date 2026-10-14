using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// Operador fijo para trabajar el Backoffice sin la API (Api:UsarDatosDePrueba = true)
public class OperadorApiDePrueba : IOperadorApi
{
    public static readonly OperadorActualDto Operador = new(
        1,
        "Operador de prueba",
        "#6F42C1",
        null,
        [
            new ReglaOperativaDto("Estandar", "Estándar", "Entregas en el día hábil siguiente",
                2, 24, 48, 72, true, false, false),
            new ReglaOperativaDto("Urgente", "Urgente", null,
                1, 4, 12, 24, true, true, false)
        ]);

    public Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default) =>
        Task.FromResult(Operador);
}
