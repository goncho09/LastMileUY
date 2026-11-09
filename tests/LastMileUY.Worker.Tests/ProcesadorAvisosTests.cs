namespace LastMileUY.Worker.Tests;

using LastMileUY.Contracts.Mensajeria;
using Npgsql;
using static EntornoDelWorker;

// Lo que hace el worker con cada evento, contra la base real: un aviso pendiente por cada suscripción
// activa del comercio a ese tipo de evento, solo del operador del evento, y una sola vez por evento.
[Collection(ColeccionEntornoDelWorker.Nombre)]
public class ProcesadorAvisosTests(EntornoDelWorker entorno)
{
    [Fact]
    public async Task Registra_un_aviso_por_cada_suscripcion_activa_del_comercio_a_ese_evento()
    {
        var evento = Evento(OperadorA, EnvioDeA);

        var resultado = await Procesador().ProcesarAsync(evento, CancellationToken.None);

        Assert.False(resultado.Repetido);
        Assert.Equal(1, resultado.AvisosRegistrados);
        Assert.Equal(1, await AvisosDeAsync(evento, SuscripcionDeA));
        // Ni la inactiva ni la del alta
        Assert.Equal(0, await AvisosDeAsync(evento, SuscripcionInactivaDeA));
        Assert.Equal(0, await AvisosDeAsync(evento, SuscripcionAlAltaDeA));
    }

    [Fact]
    public async Task Solo_ve_las_suscripciones_del_operador_del_evento()
    {
        // El comercio también está suscrito con el operador B, pero el evento es de A
        var evento = Evento(OperadorA, EnvioDeA);

        await Procesador().ProcesarAsync(evento, CancellationToken.None);

        Assert.Equal(0, await AvisosDeAsync(evento, SuscripcionDeB));
    }

    [Fact]
    public async Task El_mismo_evento_dos_veces_se_procesa_una_sola_vez()
    {
        var evento = Evento(OperadorB, EnvioDeB);
        var procesador = Procesador();

        var primero = await procesador.ProcesarAsync(evento, CancellationToken.None);
        var segundo = await procesador.ProcesarAsync(evento, CancellationToken.None);

        Assert.False(primero.Repetido);
        Assert.True(segundo.Repetido);
        Assert.Equal(1, await AvisosDeAsync(evento, SuscripcionDeB));
    }

    // Avisos apagados para el operador (docs/funcionalidades.md): el evento se procesa, pero no deja
    // avisos. Prendidos otra vez, el siguiente evento sí
    [Fact]
    public async Task Con_los_avisos_apagados_para_el_operador_no_registra_avisos()
    {
        await BanderaDeAvisosAsync(OperadorB, """
            INSERT INTO "FuncionalidadesOperador"("OperadorId", "Clave", "Activa", "FechaModificacion")
            VALUES (@operador, @clave, false, now())
            """);
        try
        {
            var apagado = Evento(OperadorB, EnvioDeB);
            var resultado = await Procesador().ProcesarAsync(apagado, CancellationToken.None);

            Assert.False(resultado.Repetido);
            Assert.Equal(0, resultado.AvisosRegistrados);
            Assert.Equal(0, await AvisosDeAsync(apagado, SuscripcionDeB));
        }
        finally
        {
            await BanderaDeAvisosAsync(OperadorB, """
                DELETE FROM "FuncionalidadesOperador" WHERE "OperadorId" = @operador AND "Clave" = @clave
                """);
        }

        var prendido = Evento(OperadorB, EnvioDeB);
        Assert.Equal(1, (await Procesador().ProcesarAsync(prendido, CancellationToken.None)).AvisosRegistrados);
    }

    [Fact]
    public async Task Si_falla_no_queda_anotado_y_se_puede_volver_a_procesar()
    {
        // El envío 102 es de B: con el operador A cargado, RLS no deja registrarle un aviso
        var evento = Evento(OperadorA, EnvioDeB);

        await Assert.ThrowsAsync<PostgresException>(() => Procesador().ProcesarAsync(evento, CancellationToken.None));

        Assert.Equal(0, await entorno.ContarAsync(
            """SELECT count(*) FROM "MensajesProcesados" WHERE "MensajeId" = @id""", ("id", evento.EventoId)));
    }

    [Fact]
    public async Task Un_evento_de_bulto_solo_genera_avisos_para_las_suscripciones_v2_a_eventos_de_bulto()
    {
        const int suscripcionABultos = 31;
        const int suscripcionABultosV1 = 33;    // no se puede crear desde la API; si existiera, no recibe nada
        await SuscripcionAsync(suscripcionABultos, TiposEvento.BultoEstadoCambiado, version: 2);
        await SuscripcionAsync(suscripcionABultosV1, TiposEvento.BultoEstadoCambiado, version: 1);

        var evento = Evento(OperadorA, EnvioDeA, TiposEvento.BultoEstadoCambiado) with
        {
            Estado = "NoEntregado",
            EstadoAnterior = "Pendiente",
            CodigoBulto = "B3",
            MotivoNoEntrega = "Bulto dañado"
        };

        await Procesador().ProcesarAsync(evento, CancellationToken.None);

        // Las suscripciones a cambios de estado del envío no reciben los de bulto, ni una v1 a bultos
        Assert.Equal(0, await AvisosDeAsync(evento, SuscripcionDeA));
        Assert.Equal(0, await AvisosV2DeAsync(evento, suscripcionABultosV1));
        Assert.Equal(1, await entorno.ContarAsync("""
            SELECT count(*) FROM "EntregasAvisos"
            WHERE "SuscripcionAvisosId" = @suscripcion AND "Contenido"->>'eventoId' = @evento
              AND "Contenido"->>'version' = '2'
              AND "Contenido"->'envio'->>'codigoSeguimiento' = 'CODIGOA'
              AND "Contenido"->'bulto'->>'codigo' = 'B3'
              AND "Contenido"->'bulto'->>'estado' = 'NoEntregado'
              AND "Contenido"->'bulto'->>'motivoNoEntrega' = 'Bulto dañado'
            """, ("suscripcion", suscripcionABultos), ("evento", evento.EventoId.ToString())));
    }

    [Fact]
    public async Task Cada_suscripcion_recibe_el_contenido_de_su_version()
    {
        const int suscripcionV2 = 32;
        await SuscripcionAsync(suscripcionV2, TiposEvento.EnvioEstadoCambiado, version: 2);
        var evento = Evento(OperadorA, EnvioDeA);

        try
        {
            await Procesador().ProcesarAsync(evento, CancellationToken.None);
        }
        finally
        {
            // Las demás pruebas cuentan los avisos de las suscripciones del entorno a este evento
            await entorno.ContarAsync(
                """WITH x AS (UPDATE "SuscripcionesAvisos" SET "Activa" = false WHERE "Id" = @id RETURNING 1) SELECT count(*) FROM x""",
                ("id", suscripcionV2));
        }

        // v1: el evento tal cual, como siempre
        Assert.Equal(1, await entorno.ContarAsync("""
            SELECT count(*) FROM "EntregasAvisos"
            WHERE "SuscripcionAvisosId" = @suscripcion AND "Contenido"->>'EventoId' = @evento
              AND "Contenido"->>'Version' = '1' AND "Contenido"->>'CodigoSeguimiento' = 'CODIGOA'
              AND "Contenido"->>'Estado' = 'EnDeposito' AND "Contenido"->>'CodigoBulto' IS NULL
            """, ("suscripcion", SuscripcionDeA), ("evento", evento.EventoId.ToString())));

        // v2: el formato nuevo, sin ids internos
        Assert.Equal(1, await entorno.ContarAsync("""
            SELECT count(*) FROM "EntregasAvisos"
            WHERE "SuscripcionAvisosId" = @suscripcion AND "Contenido"->>'eventoId' = @evento
              AND "Contenido"->>'version' = '2'
              AND "Contenido"->'envio'->>'estado' = 'EnDeposito'
              AND "Contenido"->'envio'->>'estadoAnterior' = 'Admitido'
              AND "Contenido"->'bulto' IS NULL AND "Contenido"->>'operadorId' IS NULL
              AND "Contenido"->>'envioId' IS NULL
            """, ("suscripcion", suscripcionV2), ("evento", evento.EventoId.ToString())));
    }

    // Una suscripción más del comercio con el operador A, con su versión del contrato
    private async Task SuscripcionAsync(int id, string tipo, int version)
    {
        await using var conexion = new NpgsqlConnection(entorno.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            INSERT INTO "SuscripcionesAvisos"
                ("Id", "OperadorId", "ComercioId", "TipoEvento", "UrlDestino", "Activa", "FechaCreacion",
                 "ClaveFirma", "VersionContrato")
            VALUES ({id}, {OperadorA}, {Comercio}, @tipo, 'https://comercio.test/' || {id},
                    true, now(), encode(gen_random_bytes(32), 'hex'), {version})
            ON CONFLICT ("Id") DO NOTHING
            """, conexion);
        comando.Parameters.AddWithValue("tipo", tipo);
        await comando.ExecuteNonQueryAsync();
    }

    private Task<long> AvisosV2DeAsync(EstadoEnvioCambiado evento, int suscripcion) => entorno.ContarAsync("""
        SELECT count(*) FROM "EntregasAvisos"
        WHERE "SuscripcionAvisosId" = @suscripcion
          AND ("Contenido"->>'EventoId' = @evento OR "Contenido"->>'eventoId' = @evento)
        """, ("suscripcion", suscripcion), ("evento", evento.EventoId.ToString()));

    private async Task BanderaDeAvisosAsync(int operadorId, string sql)
    {
        await using var conexion = new NpgsqlConnection(entorno.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("operador", operadorId);
        comando.Parameters.AddWithValue("clave", Contracts.Operadores.Funcionalidades.AvisosComercios);
        await comando.ExecuteNonQueryAsync();
    }

    private ProcesadorAvisos Procesador() => new(NpgsqlDataSource.Create(entorno.CadenaAplicacion));

    // Los avisos de un evento: el contenido es el evento, con su EventoId
    private Task<long> AvisosDeAsync(EstadoEnvioCambiado evento, int suscripcion) => entorno.ContarAsync("""
        SELECT count(*) FROM "EntregasAvisos"
        WHERE "SuscripcionAvisosId" = @suscripcion AND "Contenido"->>'EventoId' = @evento AND "Estado" = 1
        """, ("suscripcion", suscripcion), ("evento", evento.EventoId.ToString()));
}
