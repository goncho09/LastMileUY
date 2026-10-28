using Npgsql;

namespace LastMileUY.Worker;

// Manda por HTTP los avisos pendientes (EntregasAvisos) que deja ProcesadorAvisos (ver docs/mensajeria.md).
// - Recorre los operadores y, para cada uno, carga su operador antes de leer: el RLS del ADR-01 se le
//   aplica igual que a la API, sin un usuario que lo saltee.
// - Toma los avisos de a uno con FOR UPDATE SKIP LOCKED: con varias instancias del worker, cada aviso lo
//   manda una sola. La fila queda bloqueada mientras espera al comercio (como mucho ClienteAvisos.TiempoMaximo).
// - Anota el resultado en el aviso: entregado, o el intento fallido con su motivo.
// - Manda apenas ProcesadorAvisos registra avisos (le avisa) y, además, revisa cada tanto.
public class DespachadorAvisos(
    NpgsqlDataSource baseDeDatos,
    ClienteAvisos cliente,
    SenalAvisos senal,
    ILogger<DespachadorAvisos> logger) : BackgroundService
{
    // Cada cuánto revisa aunque nadie avise (avisos de otra instancia, reintentos programados)
    private static readonly TimeSpan Revision = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var fallosSeguidos = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnviarPendientesAsync(stoppingToken);
                fallosSeguidos = 0;
                await senal.EsperarAsync(Revision, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // La base no responde: los avisos siguen pendientes, se reintenta con espera creciente
                fallosSeguidos++;
                var espera = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, fallosSeguidos), EsperaMaxima.TotalSeconds));
                logger.LogWarning(ex, "No se pudieron mandar los avisos; se reintenta en {Espera}", espera);
                await Task.Delay(espera, stoppingToken);
            }
        }
    }

    // Manda todos los avisos que ya tocan, de todos los operadores. Devuelve cuántos intentó.
    public async Task<int> EnviarPendientesAsync(CancellationToken cancellationToken)
    {
        var intentados = 0;

        foreach (var operador in await OperadoresAsync(cancellationToken))
            while (await EnviarSiguienteAsync(operador, cancellationToken))
                intentados++;

        return intentados;
    }

    // Operadores no tiene RLS (ver AislamientoEntreOperadoresTests.TablasSinRls)
    private async Task<List<int>> OperadoresAsync(CancellationToken cancellationToken)
    {
        await using var comando = baseDeDatos.CreateCommand("""SELECT "Id" FROM "Operadores" ORDER BY "Id" """);
        await using var lector = await comando.ExecuteReaderAsync(cancellationToken);

        var operadores = new List<int>();
        while (await lector.ReadAsync(cancellationToken))
            operadores.Add(lector.GetInt32(0));

        return operadores;
    }

    // false si el operador no tiene más avisos para mandar ahora
    private async Task<bool> EnviarSiguienteAsync(int operadorId, CancellationToken cancellationToken)
    {
        await using var conexion = await baseDeDatos.OpenConnectionAsync(cancellationToken);
        await using var transaccion = await conexion.BeginTransactionAsync(cancellationToken);

        await using (var comando = new NpgsqlCommand("""
            SELECT set_config('app.operador_id', @operador, true), set_config('app.comercio_id', '', true)
            """, conexion, transaccion))
        {
            comando.Parameters.AddWithValue("operador", operadorId.ToString());
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }

        var (aviso, suscripcionActiva) = await LeerSiguienteAsync(conexion, transaccion, cancellationToken);

        if (aviso is null)
        {
            await transaccion.RollbackAsync(cancellationToken);
            return false;
        }

        using var alcance = logger.BeginScope(
            "Correlación {CorrelacionId}, aviso {AvisoId}", aviso.CorrelacionId, aviso.AvisoId);

        // Si el comercio desactivó la suscripción después de registrarse el aviso, no se manda
        var resultado = suscripcionActiva
            ? await cliente.EnviarAsync(aviso, cancellationToken)
            : new ResultadoEnvio(false, null, "La suscripción está desactivada.", TimeSpan.Zero);

        await RegistrarResultadoAsync(conexion, transaccion, aviso, resultado, cancellationToken);

        // Tarjeta "Registrar el historial de intentos": cada intento se anota acá, en la misma
        // transacción (aviso.Intento, resultado.CodigoHttp, resultado.Error, resultado.Duracion).

        await transaccion.CommitAsync(cancellationToken);

        if (resultado.Exitoso)
            logger.LogInformation(
                "Aviso {Id} entregado a {Servidor} ({Codigo}, {Duracion} ms)",
                aviso.Id, Servidor(aviso.UrlDestino), resultado.CodigoHttp, (int)resultado.Duracion.TotalMilliseconds);
        else
            logger.LogWarning(
                "Falló el intento {Intento} del aviso {Id} a {Servidor}: {Error}",
                aviso.Intento, aviso.Id, Servidor(aviso.UrlDestino), resultado.Error);

        return true;
    }

    // En los registros va solo el servidor: la URL completa puede llevar algún dato del comercio
    private static string Servidor(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Authority : "(URL inválida)";

    // Estado 1 = Pendiente (EstadoEntregaAviso). Primero los más viejos.
    private static async Task<(AvisoPendiente? Aviso, bool SuscripcionActiva)> LeerSiguienteAsync(
        NpgsqlConnection conexion, NpgsqlTransaction transaccion, CancellationToken cancellationToken)
    {
        await using var comando = new NpgsqlCommand("""
            SELECT a."Id", a."EventoId", a."TipoEvento", a."Contenido"::text, a."CantidadIntentos",
                   s."UrlDestino", a."Contenido"->>'CorrelacionId', s."Activa"
            FROM "EntregasAvisos" a
            JOIN "SuscripcionesAvisos" s ON s."Id" = a."SuscripcionAvisosId"
            WHERE a."Estado" = 1 AND (a."FechaProximoIntento" IS NULL OR a."FechaProximoIntento" <= now())
            ORDER BY a."FechaCreacion", a."Id"
            LIMIT 1
            FOR UPDATE OF a SKIP LOCKED
            """, conexion, transaccion);
        await using var lector = await comando.ExecuteReaderAsync(cancellationToken);

        if (!await lector.ReadAsync(cancellationToken))
            return (null, false);

        var aviso = new AvisoPendiente(
            Id: lector.GetInt32(0),
            AvisoId: lector.GetGuid(1),
            TipoEvento: lector.GetString(2),
            Contenido: lector.GetString(3),
            Intento: lector.GetInt32(4) + 1,
            UrlDestino: lector.GetString(5),
            CorrelacionId: lector.IsDBNull(6) ? null : lector.GetString(6));

        return (aviso, lector.GetBoolean(7));
    }

    // Estado 2 = Entregado, 3 = Fallido
    private static async Task RegistrarResultadoAsync(
        NpgsqlConnection conexion, NpgsqlTransaction transaccion, AvisoPendiente aviso, ResultadoEnvio resultado,
        CancellationToken cancellationToken)
    {
        var proximo = resultado.Exitoso ? null : ProximoIntento(aviso.Intento);

        await using var comando = new NpgsqlCommand("""
            UPDATE "EntregasAvisos" SET
                "CantidadIntentos" = @intento,
                "FechaUltimoIntento" = clock_timestamp(),
                "Estado" = CASE WHEN @exitoso THEN 2 WHEN @proximo IS NULL THEN 3 ELSE 1 END,
                "FechaEntrega" = CASE WHEN @exitoso THEN clock_timestamp() END,
                "FechaProximoIntento" = @proximo,
                "UltimoError" = left(@error, 1000)
            WHERE "Id" = @id
            """, conexion, transaccion);
        comando.Parameters.AddWithValue("id", aviso.Id);
        comando.Parameters.AddWithValue("intento", aviso.Intento);
        comando.Parameters.AddWithValue("exitoso", resultado.Exitoso);
        comando.Parameters.Add(new NpgsqlParameter<DateTime?>("proximo", proximo));
        comando.Parameters.Add(new NpgsqlParameter<string?>("error", resultado.Error));
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    // Cuándo volver a intentar después de que falló el intento número "intento"; null: no se reintenta y
    // el aviso queda Fallido. Por ahora no hay reintentos.
    // Tarjeta "Reintentos de avisos con espera creciente": se decide acá (el aviso sigue Pendiente
    // y este método lo vuelve a tomar cuando llega la fecha).
    private static DateTime? ProximoIntento(int intento) => null;
}

// Para que ProcesadorAvisos le avise al despachador que hay avisos nuevos, sin esperar a la revisión
public class SenalAvisos
{
    private readonly SemaphoreSlim senal = new(0, 1);

    public void Avisar()
    {
        try
        {
            senal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había un aviso pendiente: alcanza con uno
        }
    }

    // true si llegó un aviso antes del tiempo máximo
    public Task<bool> EsperarAsync(TimeSpan maximo, CancellationToken cancellationToken) =>
        senal.WaitAsync(maximo, cancellationToken);
}
