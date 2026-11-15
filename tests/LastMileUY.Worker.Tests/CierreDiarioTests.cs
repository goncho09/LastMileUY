using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LastMileUY.Worker.Tests;

using static EntornoDelWorker;

[Collection(ColeccionEntornoDelWorker.Nombre)]
public class CierreDiarioTests(EntornoDelWorker entorno)
{
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static readonly DateTimeOffset Ahora = new(2030, 1, 15, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Recalcula_ayer_y_hoy_por_operador_sin_duplicar_aunque_corra_dos_veces()
    {
        await using (var conexion = new NpgsqlConnection(entorno.CadenaAdministrador))
        {
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand($"""
                INSERT INTO "Envios" ("Id", "OperadorId", "ComercioId", "DestinatarioId", "CodigoSeguimiento", "FechaCreacion",
                                      "FechaEntrega", "CostoEnvio", "Estado", "Modalidad", "DireccionEntrega_Calle",
                                      "DireccionEntrega_Numero", "DireccionEntrega_Ciudad", "DireccionEntrega_Departamento")
                VALUES (901, {OperadorA}, {Comercio}, 1, 'CIERRE901', '2030-01-14 13:00Z', '2030-01-14 18:00Z', 150, 5, 1,
                        'Rivera', '1', 'Montevideo', 'Montevideo'),
                       (902, {OperadorA}, {Comercio}, 1, 'CIERRE902', '2030-01-14 14:00Z', NULL, 90.5, 6, 1,
                        'Rivera', '1', 'Montevideo', 'Montevideo'),
                       (903, {OperadorA}, {Comercio}, 1, 'CIERRE903', '2030-01-15 12:00Z', NULL, NULL, 1, 1,
                        'Rivera', '1', 'Montevideo', 'Montevideo'),
                       (904, {OperadorB}, {Comercio}, 1, 'CIERRE904', '2030-01-14 13:00Z', NULL, 70, 1, 1,
                        'Rivera', '1', 'Montevideo', 'Montevideo')
                ON CONFLICT DO NOTHING;
                """, conexion);
            await comando.ExecuteNonQueryAsync();
        }

        await using var dataSource = NpgsqlDataSource.Create(entorno.CadenaAplicacion);
        var cierre = new CierreDiario(dataSource, new RelojFijo(Ahora), new ConfigurationBuilder().Build(), NullLogger<CierreDiario>.Instance);

        await cierre.EjecutarAsync(CancellationToken.None);
        await cierre.EjecutarAsync(CancellationToken.None);

        Assert.Equal(2, await entorno.ContarAsync(
            """SELECT count(*) FROM "IndicadoresDiarios" WHERE "OperadorId" = @o AND "Dia" IN ('2030-01-14', '2030-01-15')""",
            ("o", OperadorA)));

        Assert.Equal(1, await entorno.ContarAsync(
            """SELECT count(*) FROM "IndicadoresDiarios" WHERE "OperadorId" = @o AND "Dia" = '2030-01-14' AND "Cerrado" AND "EnviosCreados" = 2 AND "Entregados" = 1 AND "EntregadosATiempo" = 1 AND "NoEntregados" = 1 AND "Importe" = 240.50""",
            ("o", OperadorA)));

        Assert.Equal(1, await entorno.ContarAsync(
            """SELECT count(*) FROM "IndicadoresDiarios" WHERE "OperadorId" = @o AND "Dia" = '2030-01-15' AND NOT "Cerrado" AND "EnviosCreados" = 1""",
            ("o", OperadorA)));

        Assert.Equal(1, await entorno.ContarAsync(
            """SELECT count(*) FROM "IndicadoresDiarios" WHERE "OperadorId" = @o AND "Dia" = '2030-01-14' AND "EnviosCreados" = 1 AND "Importe" = 70""",
            ("o", OperadorB)));
    }
}
