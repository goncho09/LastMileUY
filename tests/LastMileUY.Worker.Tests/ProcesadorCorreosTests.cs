using System.Collections.Concurrent;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LastMileUY.Worker.Tests;

using static EntornoDelWorker;

[Collection(ColeccionEntornoDelWorker.Nombre)]
public class ProcesadorCorreosTests(EntornoDelWorker entorno) : IAsyncLifetime
{
    private const int DestinatarioConEmail = 301;
    private const int DestinatarioConEmailInvalido = 302;
    private const int EnvioConEmail = 301;
    private const int EnvioConEmailInvalido = 302;

    private readonly EnviadorDePrueba enviador = new();
    private NpgsqlDataSource dataSource = null!;
    private ProcesadorCorreos procesador = null!;

    public async Task InitializeAsync()
    {
        dataSource = NpgsqlDataSource.Create(entorno.CadenaAplicacion);

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Correo:UrlSeguimiento"] = "https://seguimiento.test" })
            .Build();

        procesador = new ProcesadorCorreos(
            new RepositorioCorreos(dataSource), enviador, configuracion, NullLogger<ProcesadorCorreos>.Instance);

        await using var conexion = new NpgsqlConnection(entorno.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            INSERT INTO "Destinatarios" ("Id", "ComercioId", "Nombre", "Apellido", "Email", "FechaCreacion")
            VALUES ({DestinatarioConEmail}, {Comercio}, 'Lucía', 'Pérez', 'lucia@correo.test', now()),
                   ({DestinatarioConEmailInvalido}, {Comercio}, 'Juan', 'Gómez', 'no-es-un-email', now())
            ON CONFLICT DO NOTHING;

            INSERT INTO "Envios" ("Id", "OperadorId", "ComercioId", "DestinatarioId", "CodigoSeguimiento", "FechaCreacion",
                                  "Estado", "Modalidad", "DireccionEntrega_Calle", "DireccionEntrega_Numero",
                                  "DireccionEntrega_Ciudad", "DireccionEntrega_Departamento")
            VALUES ({EnvioConEmail}, {OperadorA}, {Comercio}, {DestinatarioConEmail}, 'CORREO301', now(), 1, 1,
                    'Rivera', '1234', 'Montevideo', 'Montevideo'),
                   ({EnvioConEmailInvalido}, {OperadorA}, {Comercio}, {DestinatarioConEmailInvalido}, 'CORREO302', now(), 1, 1,
                    'Rivera', '1234', 'Montevideo', 'Montevideo')
            ON CONFLICT DO NOTHING;
            """, conexion);
        await comando.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await dataSource.DisposeAsync();

    private static EstadoEnvioCambiado Evento(
        int envioId, string estado, string tipo = TiposEvento.EnvioEstadoCambiado, bool esPublico = true, int operadorId = OperadorA) =>
        EntornoDelWorker.Evento(operadorId, envioId, tipo) with
        {
            Estado = estado,
            CodigoSeguimiento = $"CORREO{envioId}",
            EsPublico = esPublico
        };

    private Task<long> ContarCorreosAsync(Guid eventoId, string estado) =>
        entorno.ContarAsync("""SELECT count(*) FROM "CorreosSeguimiento" WHERE "EventoId" = @evento AND "Estado" = @estado""",
            ("evento", eventoId), ("estado", estado));

    [Fact]
    public async Task El_alta_manda_un_correo_y_repetir_el_evento_no_lo_manda_de_nuevo()
    {
        var evento = Evento(EnvioConEmail, "Admitido", TiposEvento.EnvioAdmitido);

        Assert.Equal(ResultadoCorreo.Enviado, await procesador.ProcesarAsync(evento, CancellationToken.None));
        Assert.Equal(ResultadoCorreo.YaProcesado, await procesador.ProcesarAsync(evento, CancellationToken.None));

        var (destinatario, correo) = Assert.Single(enviador.Enviados, e => e.Correo.Texto.Contains("CORREO301"));
        Assert.Equal("lucia@correo.test", destinatario);
        Assert.Contains("https://seguimiento.test/seguimiento/CORREO301", correo.Html);
        Assert.Equal(1, await ContarCorreosAsync(evento.EventoId, "Enviado"));
    }

    [Fact]
    public async Task Sin_email_no_se_manda_ni_queda_como_enviado_y_no_se_reintenta()
    {
        var evento = Evento(EnvioDeA, "Admitido", TiposEvento.EnvioAdmitido);

        Assert.Equal(ResultadoCorreo.SinDestinatario, await procesador.ProcesarAsync(evento, CancellationToken.None));
        Assert.Equal(ResultadoCorreo.YaProcesado, await procesador.ProcesarAsync(evento, CancellationToken.None));

        Assert.DoesNotContain(enviador.Enviados, e => e.Correo.Texto.Contains("CODIGOA"));
        Assert.Equal(1, await ContarCorreosAsync(evento.EventoId, "SinDestinatario"));
        Assert.Equal(0, await ContarCorreosAsync(evento.EventoId, "Enviado"));
    }

    [Fact]
    public async Task Un_email_invalido_o_un_envio_de_otro_operador_no_se_intentan_mandar()
    {
        var invalido = Evento(EnvioConEmailInvalido, "EnTransito");
        var ajeno = Evento(EnvioConEmail, "EnTransito", operadorId: OperadorB);

        Assert.Equal(ResultadoCorreo.SinDestinatario, await procesador.ProcesarAsync(invalido, CancellationToken.None));
        Assert.Equal(ResultadoCorreo.SinDestinatario, await procesador.ProcesarAsync(ajeno, CancellationToken.None));
        Assert.DoesNotContain(enviador.Enviados, e => e.Destinatario == "no-es-un-email");
    }

    [Theory]
    [InlineData("EnTransito", "Tu envío está en camino")]
    [InlineData("Entregado", "Entregamos tu envío")]
    [InlineData("NoEntregado", "No pudimos entregar tu envío")]
    [InlineData("EnDevolucion", "Tu envío vuelve al comercio")]
    public async Task Los_cambios_de_estado_que_ve_el_destinatario_mandan_su_propio_correo(string estado, string asunto)
    {
        var evento = Evento(EnvioConEmail, estado);

        Assert.Equal(ResultadoCorreo.Enviado, await procesador.ProcesarAsync(evento, CancellationToken.None));
        Assert.Contains(enviador.Enviados, e => e.Correo.Asunto.StartsWith(asunto) && e.Correo.Texto.Contains("CORREO301"));
    }

    [Fact]
    public async Task Los_estados_internos_y_los_eventos_no_publicos_no_mandan_correo()
    {
        var interno = Evento(EnvioConEmail, "EnDeposito");
        var noPublico = Evento(EnvioConEmail, "Entregado", esPublico: false);
        var bulto = Evento(EnvioConEmail, "Entregado", TiposEvento.BultoEstadoCambiado);

        Assert.Equal(ResultadoCorreo.Ignorado, await procesador.ProcesarAsync(interno, CancellationToken.None));
        Assert.Equal(ResultadoCorreo.Ignorado, await procesador.ProcesarAsync(noPublico, CancellationToken.None));
        Assert.Equal(ResultadoCorreo.Ignorado, await procesador.ProcesarAsync(bulto, CancellationToken.None));
        Assert.Equal(0, await entorno.ContarAsync(
            """SELECT count(*) FROM "CorreosSeguimiento" WHERE "EventoId" = ANY(@eventos)""",
            ("eventos", new[] { interno.EventoId, noPublico.EventoId, bulto.EventoId })));
    }

    [Fact]
    public async Task Si_el_servidor_de_correo_falla_queda_fallido_y_el_reintento_lo_manda()
    {
        var evento = Evento(EnvioConEmail, "Reprogramado");
        enviador.FallarProximo = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => procesador.ProcesarAsync(evento, CancellationToken.None));
        Assert.Equal(1, await ContarCorreosAsync(evento.EventoId, "Fallido"));

        Assert.Equal(ResultadoCorreo.Enviado, await procesador.ProcesarAsync(evento, CancellationToken.None));
        Assert.Equal(1, await ContarCorreosAsync(evento.EventoId, "Enviado"));
    }

    private sealed class EnviadorDePrueba : IEnviadorCorreo
    {
        public ConcurrentBag<(string Destinatario, CorreoArmado Correo)> Enviados { get; } = [];

        public bool FallarProximo { get; set; }

        public Task EnviarAsync(string destinatario, CorreoArmado correo, CancellationToken cancellationToken = default)
        {
            if (FallarProximo)
            {
                FallarProximo = false;
                throw new InvalidOperationException("SMTP no disponible");
            }

            Enviados.Add((destinatario, correo));
            return Task.CompletedTask;
        }
    }
}
