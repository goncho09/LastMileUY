using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using LastMileUY.Contracts.Operadores;
using Npgsql;

namespace LastMileUY.Worker;

public interface IProcesadorEventos
{
    Task<ResultadoProceso> ProcesarAsync(EstadoEnvioCambiado evento, CancellationToken cancellationToken);
}

public record ResultadoProceso(bool Repetido, int AvisosRegistrados);

public class ProcesadorAvisos(NpgsqlDataSource baseDeDatos, SenalAvisos? senal = null) : IProcesadorEventos
{
    public const string Consumidor = "avisos";

    public async Task<ResultadoProceso> ProcesarAsync(EstadoEnvioCambiado evento, CancellationToken cancellationToken)
    {
        await using var conexion = await baseDeDatos.OpenConnectionAsync(cancellationToken);
        await using var transaccion = await conexion.BeginTransactionAsync(cancellationToken);

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

        var avisos = await EjecutarAsync(conexion, transaccion, """
            INSERT INTO "EntregasAvisos"("SuscripcionAvisosId", "EnvioId", "EventoId", "TipoEvento", "Contenido",
                    "Estado", "CantidadIntentos", "FechaCreacion", "FechaProximoIntento")
            SELECT s."Id", @envio, gen_random_uuid(), @tipo,
                   CASE WHEN s."VersionContrato" = 2 THEN @contenidoV2::jsonb ELSE @contenido::jsonb END,
                   1, 0, now(), now()
            FROM "SuscripcionesAvisos" s
            WHERE s."Activa" AND s."ComercioId" = @comercio AND s."TipoEvento" = @tipo
              AND (s."VersionContrato" = 2 OR @tipo <> @tipoBulto)
              -- Avisos apagados para el operador (docs/funcionalidades.md): no se registran avisos nuevos.
              -- Sin decisión guardada vale el valor por defecto
              AND COALESCE((SELECT f."Activa" FROM "FuncionalidadesOperador" f
                            WHERE f."OperadorId" = @operador AND f."Clave" = @funcionalidad), @porDefecto)
            """,
            ("envio", evento.EnvioId), ("tipo", evento.Tipo), ("comercio", evento.ComercioId),
            ("tipoBulto", TiposEvento.BultoEstadoCambiado),
            ("operador", evento.OperadorId),
            ("funcionalidad", Funcionalidades.AvisosComercios),
            ("porDefecto", Funcionalidades.Buscar(Funcionalidades.AvisosComercios)!.PorDefecto),
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
