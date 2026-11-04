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
    public async Task Un_evento_de_bulto_solo_genera_avisos_para_las_suscripciones_a_eventos_de_bulto()
    {
        const int suscripcionABultos = 31;
        await using (var conexion = new NpgsqlConnection(entorno.CadenaAdministrador))
        {
            await conexion.OpenAsync();
            await using var comando = new NpgsqlCommand($"""
                INSERT INTO "SuscripcionesAvisos"
                    ("Id", "OperadorId", "ComercioId", "TipoEvento", "UrlDestino", "Activa", "FechaCreacion", "ClaveFirma")
                VALUES ({suscripcionABultos}, {OperadorA}, {Comercio}, @tipo, 'https://comercio.test/bultos',
                        true, now(), encode(gen_random_bytes(32), 'hex'))
                ON CONFLICT ("Id") DO NOTHING
                """, conexion);
            comando.Parameters.AddWithValue("tipo", TiposEvento.BultoEstadoCambiado);
            await comando.ExecuteNonQueryAsync();
        }

        var evento = Evento(OperadorA, EnvioDeA, TiposEvento.BultoEstadoCambiado) with
        {
            Estado = "NoEntregado",
            EstadoAnterior = "Pendiente",
            CodigoBulto = "B3",
            MotivoNoEntrega = "Bulto dañado"
        };

        await Procesador().ProcesarAsync(evento, CancellationToken.None);

        // Las suscripciones a cambios de estado del envío no reciben los de bulto
        Assert.Equal(0, await AvisosDeAsync(evento, SuscripcionDeA));
        Assert.Equal(1, await entorno.ContarAsync("""
            SELECT count(*) FROM "EntregasAvisos"
            WHERE "SuscripcionAvisosId" = @suscripcion AND "Contenido"->>'EventoId' = @evento
              AND "Contenido"->>'CodigoBulto' = 'B3' AND "Contenido"->>'MotivoNoEntrega' = 'Bulto dañado'
            """, ("suscripcion", suscripcionABultos), ("evento", evento.EventoId.ToString())));
    }

    private ProcesadorAvisos Procesador() => new(NpgsqlDataSource.Create(entorno.CadenaAplicacion));

    // Los avisos de un evento: el contenido es el evento, con su EventoId
    private Task<long> AvisosDeAsync(EstadoEnvioCambiado evento, int suscripcion) => entorno.ContarAsync("""
        SELECT count(*) FROM "EntregasAvisos"
        WHERE "SuscripcionAvisosId" = @suscripcion AND "Contenido"->>'EventoId' = @evento AND "Estado" = 1
        """, ("suscripcion", suscripcion), ("evento", evento.EventoId.ToString()));
}
