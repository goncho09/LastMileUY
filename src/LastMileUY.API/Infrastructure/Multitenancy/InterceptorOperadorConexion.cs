using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Multitenancy;

// Cada vez que EF abre una conexión, carga en PostgreSQL la variable app.operador_id.
// Las políticas RLS comparan contra esa variable (segunda barrera del ADR-01).
public class InterceptorOperadorConexion(IContextoOperador contextoOperador) : DbConnectionInterceptor
{
    private const string Sql = "SELECT set_config('app.operador_id', @operador, false)";

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

        // Sin operador se carga vacío: la política lo toma como "ninguno" y no devuelve filas
        var parametro = comando.CreateParameter();
        parametro.ParameterName = "operador";
        parametro.Value = contextoOperador.OperadorId?.ToString() ?? string.Empty;
        comando.Parameters.Add(parametro);

        return comando;
    }
}
