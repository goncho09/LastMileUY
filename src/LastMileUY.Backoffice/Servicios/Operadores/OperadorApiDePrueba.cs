using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Backoffice.Servicios;

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

    private readonly List<ReglaEntregaParcialDto> reglasEntregaParcial =
    [
        new(1, 1, true, "NuevaHojaDeRuta" ,0m,"Versión inicial de los datos de prueba" ,"Operador de prueba",
            DateTime.UtcNow.AddDays(-7))
    ];

    private readonly Lock candado = new();

    private Coordenadas? deposito = new(-34.859000m, -56.211500m);

    public Task<OperadorActualDto> ObtenerActualAsync(CancellationToken ct = default)
    {
        lock (candado)
        {
            return Task.FromResult(Operador with { Deposito = deposito });
        }
    }

    public Task GuardarDepositoAsync(Coordenadas? nuevo, CancellationToken ct = default)
    {
        if (nuevo is not null && !Coordenadas.EsValida(nuevo.Latitud, nuevo.Longitud))
        {
            throw new OperacionRechazadaException("La latitud va de -90 a 90 y la longitud de -180 a 180.");
        }

        lock (candado)
        {
            deposito = nuevo;
        }

        return Task.CompletedTask;
    }

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
                request.DestinoBultosPendientes, 0m, request.Motivo.Trim(), "Operador de prueba", DateTime.UtcNow);

            reglasEntregaParcial.Insert(0, regla);
            return Task.FromResult(regla);
        }
    }
}
