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

    // En memoria, la más nueva primero (se pierde al reiniciar)
    private readonly List<ReglaEntregaParcialDto> reglasEntregaParcial =
    [
        new(1, 1, true, "NuevaHojaDeRuta", "Versión inicial de los datos de prueba", "Operador de prueba",
            DateTime.UtcNow.AddDays(-7))
    ];

    private readonly Lock candado = new();

    public Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default) =>
        Task.FromResult(Operador);

    public Task<IReadOnlyList<ReglaEntregaParcialDto>> ListarReglasEntregaParcialAsync(CancellationToken ct = default)
    {
        lock (candado)
        {
            return Task.FromResult<IReadOnlyList<ReglaEntregaParcialDto>>([.. reglasEntregaParcial]);
        }
    }

    public Task<ReglaEntregaParcialDto> PublicarReglaEntregaParcialAsync(
        PublicarReglaEntregaParcialRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Motivo))
        {
            throw new OperacionRechazadaException("Contá por qué se cambian las reglas: queda en el historial.");
        }

        lock (candado)
        {
            var version = reglasEntregaParcial[0].Version + 1;
            var regla = new ReglaEntregaParcialDto(version, version, request.PermiteEntregaParcial,
                request.DestinoBultosPendientes, request.Motivo.Trim(), "Operador de prueba", DateTime.UtcNow);

            reglasEntregaParcial.Insert(0, regla);
            return Task.FromResult(regla);
        }
    }
}
