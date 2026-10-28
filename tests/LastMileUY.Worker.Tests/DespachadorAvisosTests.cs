namespace LastMileUY.Worker.Tests;

using System.Text.Json;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static EntornoDelWorker;

// El envío de los avisos pendientes, contra la base real y el receptor de prueba levantado de verdad
[Collection(ColeccionEntornoDelWorker.Nombre)]
public class DespachadorAvisosTests(EntornoDelWorker entorno) : IAsyncLifetime
{
    // Suscripciones del comercio al receptor (además de las del entorno). Se actualizan en cada prueba
    // con la dirección del receptor, que cambia.
    private const int SuscripcionAlReceptorDeA = 11;
    private const int SuscripcionAlReceptorDeB = 12;
    private const int SuscripcionConErrorDeA = 13;      // el receptor responde 500
    private const int SuscripcionInactivaDeA = 14;
    private const int SuscripcionLentaDeA = 15;         // el receptor tarda un segundo

    private readonly ReceptorDePrueba receptor = new();

    public async Task InitializeAsync()
    {
        await using var conexion = new NpgsqlConnection(entorno.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            INSERT INTO "SuscripcionesAvisos"("Id", "OperadorId", "ComercioId", "TipoEvento", "UrlDestino", "Activa", "FechaCreacion")
            VALUES ({SuscripcionAlReceptorDeA}, {OperadorA}, {Comercio}, @tipo, @a, true, now()),
                   ({SuscripcionAlReceptorDeB}, {OperadorB}, {Comercio}, @tipo, @b, true, now()),
                   ({SuscripcionConErrorDeA}, {OperadorA}, {Comercio}, @tipo, @error, true, now()),
                   ({SuscripcionInactivaDeA}, {OperadorA}, {Comercio}, @tipo, @inactiva, false, now()),
                   ({SuscripcionLentaDeA}, {OperadorA}, {Comercio}, @tipo, @lenta, true, now())
            ON CONFLICT ("Id") DO UPDATE SET "UrlDestino" = EXCLUDED."UrlDestino"
            """, conexion);
        comando.Parameters.AddWithValue("tipo", TiposEvento.EnvioEstadoCambiado);
        comando.Parameters.AddWithValue("a", receptor.Url("avisos/a"));
        comando.Parameters.AddWithValue("b", receptor.Url("avisos/b"));
        comando.Parameters.AddWithValue("error", receptor.Url("avisos/error?estado=500"));
        comando.Parameters.AddWithValue("inactiva", receptor.Url("avisos/inactiva"));
        comando.Parameters.AddWithValue("lenta", receptor.Url("avisos/lenta?demora=1"));
        await comando.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Entrega_el_aviso_y_lo_marca_como_entregado()
    {
        var aviso = await RegistrarAvisoAsync(SuscripcionAlReceptorDeA, EnvioDeA);

        await Despachador().EnviarPendientesAsync(CancellationToken.None);

        var recibido = Assert.Single(await RecibidosAsync(aviso));
        Assert.Equal("/avisos/a", recibido.Ruta);
        Assert.Equal("1", recibido.Encabezados[EncabezadosAvisos.Intento]);
        Assert.Equal("prueba-worker", recibido.Encabezados[TopologiaMensajeria.EncabezadoCorrelacion]);
        Assert.Equal(aviso.ToString(), JsonDocument.Parse(recibido.Contenido).RootElement.GetProperty("AvisoDePrueba").GetString());
        Assert.Equal(1, await AvisosAsync(aviso, """
            "Estado" = 2 AND "CantidadIntentos" = 1 AND "FechaEntrega" IS NOT NULL
            AND "FechaUltimoIntento" IS NOT NULL AND "UltimoError" IS NULL AND "FechaProximoIntento" IS NULL
            """));
    }

    [Fact]
    public async Task Manda_los_avisos_de_todos_los_operadores()
    {
        // Cada operador con su RLS: el despachador carga uno por vez
        var deA = await RegistrarAvisoAsync(SuscripcionAlReceptorDeA, EnvioDeA);
        var deB = await RegistrarAvisoAsync(SuscripcionAlReceptorDeB, EnvioDeB);

        await Despachador().EnviarPendientesAsync(CancellationToken.None);

        Assert.Equal(1, await AvisosAsync(deA, """ "Estado" = 2 """));
        Assert.Equal(1, await AvisosAsync(deB, """ "Estado" = 2 """));
        Assert.Equal("/avisos/b", Assert.Single(await RecibidosAsync(deB)).Ruta);
    }

    [Fact]
    public async Task Si_el_comercio_responde_con_error_queda_fallido_con_el_motivo()
    {
        var aviso = await RegistrarAvisoAsync(SuscripcionConErrorDeA, EnvioDeA);

        await Despachador().EnviarPendientesAsync(CancellationToken.None);

        Assert.Single(await RecibidosAsync(aviso));
        // Sin reintentos por ahora: queda Fallido (3), con el intento y el motivo anotados
        Assert.Equal(1, await AvisosAsync(aviso, """
            "Estado" = 3 AND "CantidadIntentos" = 1 AND "UltimoError" LIKE '%500%' AND "FechaEntrega" IS NULL
            """));
    }

    [Fact]
    public async Task No_manda_los_avisos_de_una_suscripcion_desactivada()
    {
        var aviso = await RegistrarAvisoAsync(SuscripcionInactivaDeA, EnvioDeA);

        await Despachador().EnviarPendientesAsync(CancellationToken.None);

        Assert.Empty(await RecibidosAsync(aviso));
        Assert.Equal(1, await AvisosAsync(aviso, """ "Estado" = 3 AND "UltimoError" LIKE '%desactivada%' """));
    }

    [Fact]
    public async Task No_toma_los_avisos_que_todavia_no_tocan()
    {
        // Como quedará uno programado para reintentar más tarde
        var aviso = await RegistrarAvisoAsync(SuscripcionAlReceptorDeA, EnvioDeA, DateTime.UtcNow.AddHours(1));

        await Despachador().EnviarPendientesAsync(CancellationToken.None);

        Assert.Empty(await RecibidosAsync(aviso));
        Assert.Equal(1, await AvisosAsync(aviso, """ "Estado" = 1 AND "CantidadIntentos" = 0 """));
    }

    [Fact]
    public async Task Con_dos_despachadores_a_la_vez_cada_aviso_sale_una_sola_vez()
    {
        // Como dos instancias del worker: el receptor tarda, así que trabajan al mismo tiempo
        var avisos = new List<Guid>();
        for (var i = 0; i < 4; i++)
            avisos.Add(await RegistrarAvisoAsync(SuscripcionLentaDeA, EnvioDeA));

        await Task.WhenAll(
            Despachador().EnviarPendientesAsync(CancellationToken.None),
            Despachador().EnviarPendientesAsync(CancellationToken.None));

        foreach (var aviso in avisos)
        {
            Assert.Single(await RecibidosAsync(aviso));
            Assert.Equal(1, await AvisosAsync(aviso, """ "Estado" = 2 AND "CantidadIntentos" = 1 """));
        }
    }

    [Fact]
    public async Task Al_registrar_avisos_el_procesador_le_avisa_al_despachador()
    {
        var senal = new SenalAvisos();

        await new ProcesadorAvisos(NpgsqlDataSource.Create(entorno.CadenaAplicacion), senal)
            .ProcesarAsync(Evento(OperadorA, EnvioDeA), CancellationToken.None);

        Assert.True(await senal.EsperarAsync(TimeSpan.Zero, CancellationToken.None));
    }

    private DespachadorAvisos Despachador() => new(
        NpgsqlDataSource.Create(entorno.CadenaAplicacion),
        new ClienteAvisos(new HttpClient(ClienteAvisos.CrearManejador(permitirLocales: true))),
        new SenalAvisos(),
        NullLogger<DespachadorAvisos>.Instance);

    // Un aviso pendiente, como lo deja ProcesadorAvisos. Devuelve su identificador (X-Aviso-Id).
    private async Task<Guid> RegistrarAvisoAsync(int suscripcion, int envio, DateTime? proximoIntento = null)
    {
        var avisoId = Guid.NewGuid();
        var evento = Evento(envio == EnvioDeA ? OperadorA : OperadorB, envio);
        // Una marca propia en el contenido, para reconocerlo en lo que recibe el receptor
        var contenido = JsonSerializer.SerializeToNode(evento)!;
        contenido["AvisoDePrueba"] = avisoId.ToString();

        await using var conexion = new NpgsqlConnection(entorno.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            INSERT INTO "EntregasAvisos"("SuscripcionAvisosId", "EnvioId", "EventoId", "TipoEvento", "Contenido",
                    "Estado", "CantidadIntentos", "FechaCreacion", "FechaProximoIntento")
            VALUES (@suscripcion, @envio, @aviso, @tipo, @contenido::jsonb, 1, 0, now(), @proximo)
            """, conexion);
        comando.Parameters.AddWithValue("suscripcion", suscripcion);
        comando.Parameters.AddWithValue("envio", envio);
        comando.Parameters.AddWithValue("aviso", avisoId);
        comando.Parameters.AddWithValue("tipo", evento.Tipo);
        comando.Parameters.AddWithValue("contenido", contenido.ToJsonString());
        comando.Parameters.Add(new NpgsqlParameter<DateTime?>("proximo", proximoIntento));
        await comando.ExecuteNonQueryAsync();

        return avisoId;
    }

    private async Task<List<AvisoRecibido>> RecibidosAsync(Guid aviso) =>
        (await receptor.RecibidosAsync())
            .Where(r => r.Encabezados.GetValueOrDefault(EncabezadosAvisos.AvisoId) == aviso.ToString())
            .ToList();

    private Task<long> AvisosAsync(Guid aviso, string condicion) => entorno.ContarAsync(
        $"""SELECT count(*) FROM "EntregasAvisos" WHERE "EventoId" = @aviso AND {condicion}""", ("aviso", aviso));

    public async Task DisposeAsync() => await receptor.DisposeAsync();
}
