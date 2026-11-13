using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LastMileUY.API.Infrastructure.Multitenancy;

public class InterceptorOperadorConexion(IContextoOperador contextoOperador) : DbConnectionInterceptor
{
    // Cada conexión arranca con el operador del pedido: las políticas RLS de PostgreSQL filtran con estas variables.
    private const string Sql =
        "SELECT set_config('app.operador_id', @operador, false), set_config('app.comercio_id', @comercio, false), " +
        "set_config('app.codigo_seguimiento', '', false), set_config('app.hash_clave_api', '', false), " +
        "set_config('app.ambiente_pruebas', @pruebas, false)";

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

        AgregarParametro(comando, "operador", contextoOperador.OperadorId);
        AgregarParametro(comando, "comercio", contextoOperador.ComercioId);

        var pruebas = comando.CreateParameter();
        pruebas.ParameterName = "pruebas";
        pruebas.Value = contextoOperador.EnAmbientePruebas ? "1" : string.Empty;
        comando.Parameters.Add(pruebas);

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
