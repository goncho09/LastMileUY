using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using Npgsql;

namespace LastMileUY.Worker;

// Lo que el worker hace con cada evento (lo llama ConsumidorEventosEnvio).
public interface IProcesadorEventos
{
    Task<ResultadoProceso> ProcesarAsync(EstadoEnvioCambiado evento, CancellationToken cancellationToken);
}

// Repetido: el evento ya se había procesado y esta vez no se hizo nada
public record ResultadoProceso(bool Repetido, int AvisosRegistrados);

// Por cada evento, deja un aviso pendiente (EntregasAvisos) para cada suscripción activa del comercio a
// ese tipo de evento. Los manda DespachadorAvisos, al que le avisa apenas los registra.
// - Usa el usuario de la aplicación (lastmileuy_app) y carga el operador del evento antes de nada: le
//   aplica el RLS del ADR-01 como a la API, y solo ve las suscripciones de ese operador.
// - Todo en una transacción: anota el evento en MensajesProcesados y registra los avisos. Si el evento
//   ya estaba anotado (RabbitMQ lo entregó de nuevo), no hace nada. Si algo falla, no queda ni la
//   anotación ni los avisos, y el reintento empieza de cero.
public class ProcesadorAvisos(NpgsqlDataSource baseDeDatos, SenalAvisos? senal = null) : IProcesadorEventos
{
    public const string Consumidor = "avisos";

    public async Task<ResultadoProceso> ProcesarAsync(EstadoEnvioCambiado evento, CancellationToken cancellationToken)
    {
        await using var conexion = await baseDeDatos.OpenConnectionAsync(cancellationToken);
        await using var transaccion = await conexion.BeginTransactionAsync(cancellationToken);

        // true: solo para esta transacción. Sin comercio: el worker actúa como el operador
        await EjecutarAsync(conexion, transaccion, """
            SELECT set_config('app.operador_id', @operador, true), set_config('app.comercio_id', '', true)
            """, ("operador", evento.OperadorId.ToString()));

        var anotado = await EjecutarAsync(conexion, transaccion, """
            INSERT INTO "MensajesProcesados"("MensajeId", "Consumidor", "OperadorId", "FechaProcesado")
            VALUES (@mensaje, @consumidor, @operador, now())
            ON CONFLICT DO NOTHING
            """, ("mensaje", evento.EventoId), ("consumidor", Consumidor), ("operador", evento.OperadorId));

        if (anotado == 0)
        {
            await transaccion.RollbackAsync(cancellationToken);
            return new ResultadoProceso(Repetido: true, AvisosRegistrados: 0);
        }

        // Estado 1 = Pendiente (EstadoEntregaAviso). Sin datos personales.
        // El contenido depende de la versión del contrato de cada suscripción (VersionesAvisos): v1 es el
        // evento tal cual, como siempre; v2 es el formato nuevo. Una suscripción v1 nunca recibe tipos nuevos
        // (los de bulto): solo se puede suscribir a ellos con la v2.
        // EventoId del aviso: uno propio por cada aviso, para que el comercio descarte los repetidos.
        var avisos = await EjecutarAsync(conexion, transaccion, """
            INSERT INTO "EntregasAvisos"("SuscripcionAvisosId", "EnvioId", "EventoId", "TipoEvento", "Contenido",
                    "Estado", "CantidadIntentos", "FechaCreacion", "FechaProximoIntento")
            SELECT s."Id", @envio, gen_random_uuid(), @tipo,
                   CASE WHEN s."VersionContrato" = 2 THEN @contenidoV2::jsonb ELSE @contenido::jsonb END,
                   1, 0, now(), now()
            FROM "SuscripcionesAvisos" s
            WHERE s."Activa" AND s."ComercioId" = @comercio AND s."TipoEvento" = @tipo
              AND (s."VersionContrato" = 2 OR @tipo <> @tipoBulto)
            """,
            ("envio", evento.EnvioId), ("tipo", evento.Tipo), ("comercio", evento.ComercioId),
            ("tipoBulto", TiposEvento.BultoEstadoCambiado),
            ("contenido", VersionesAvisos.Contenido(VersionesAvisos.V1, evento)),
            ("contenidoV2", VersionesAvisos.Contenido(VersionesAvisos.V2, evento)));

        await transaccion.CommitAsync(cancellationToken);

        if (avisos > 0)
            senal?.Avisar();

        return new ResultadoProceso(Repetido: false, AvisosRegistrados: avisos);
    }

    private static async Task<int> EjecutarAsync(
        NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);

        foreach (var (nombre, valor) in parametros)
            comando.Parameters.AddWithValue(nombre, valor);

        return await comando.ExecuteNonQueryAsync();
    }
}
