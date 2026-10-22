using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Multitenancy;

// Cada vez que EF abre una conexión, carga en PostgreSQL las variables app.operador_id y app.comercio_id.
// Las políticas RLS comparan contra esas variables (segunda barrera del ADR-01).
// Se cargan las dos siempre: la conexión vuelve al pool y la usa otro pedido.
// app.codigo_seguimiento (seguimiento público) se vacía por lo mismo: el servicio que la usa la carga
// solo dentro de su transacción, y esto es una barrera más por si alguna vez quedara cargada.
public class InterceptorOperadorConexion(IContextoOperador contextoOperador) : DbConnectionInterceptor
{
    private const string Sql =
        "SELECT set_config('app.operador_id', @operador, false), set_config('app.comercio_id', @comercio, false), " +
        "set_config('app.codigo_seguimiento', '', false)";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var comando = CrearComando(connection);
        comando.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var comando = CrearComando(connection);
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CrearComando(DbConnection connection)
    {
        var comando = connection.CreateCommand();
        comando.CommandText = Sql;

        // Vacío = ninguno. Sin operador la política no devuelve filas; sin comercio no filtra por comercio.
        AgregarParametro(comando, "operador", contextoOperador.OperadorId);
        AgregarParametro(comando, "comercio", contextoOperador.ComercioId);

        return comando;
    }

    private static void AgregarParametro(DbCommand comando, string nombre, int? valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nombre;
        parametro.Value = valor?.ToString() ?? string.Empty;
        comando.Parameters.Add(parametro);
    }
}
