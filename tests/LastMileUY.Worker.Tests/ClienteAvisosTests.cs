namespace LastMileUY.Worker.Tests;

using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;

// El pedido HTTP de cada aviso, contra el receptor de prueba levantado de verdad
public class ClienteAvisosTests : IAsyncLifetime
{
    private ReceptorDePrueba receptor = null!;

    public Task InitializeAsync()
    {
        receptor = new ReceptorDePrueba();

        receptor.RegistrarClave(15, new string('A', 64));

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Manda_el_evento_con_sus_encabezados()
    {
        var aviso = Aviso(receptor.Url("avisos/comercio-1"));

        var resultado = await Cliente(permitirLocales: true).EnviarAsync(aviso, CancellationToken.None);

        Assert.True(resultado.Exitoso);
        Assert.Equal(200, resultado.CodigoHttp);
        var recibido = Assert.Single(await receptor.RecibidosAsync());
        Assert.Equal("/avisos/comercio-1", recibido.Ruta);
        Assert.Equal(aviso.Contenido, recibido.Contenido);
        Assert.Equal(aviso.AvisoId.ToString(), recibido.Encabezados[EncabezadosAvisos.AvisoId]);
        Assert.Equal(TiposEvento.EnvioEstadoCambiado, recibido.Encabezados[EncabezadosAvisos.Tipo]);
        Assert.Equal("2", recibido.Encabezados[EncabezadosAvisos.Intento]);
        Assert.Equal("prueba-avisos", recibido.Encabezados[TopologiaMensajeria.EncabezadoCorrelacion]);
    }

    [Fact]
    public async Task Una_respuesta_que_no_es_2xx_es_un_fallo_con_el_codigo()
    {
        var resultado = await Cliente(permitirLocales: true)
            .EnviarAsync(Aviso(receptor.Url("avisos?estado=503")), CancellationToken.None);

        Assert.False(resultado.Exitoso);
        Assert.Equal(503, resultado.CodigoHttp);
        Assert.Contains("503", resultado.Error);
    }

    [Fact]
    public async Task Si_el_comercio_no_responde_a_tiempo_es_un_fallo()
    {
        var resultado = await Cliente(permitirLocales: true, TimeSpan.FromMilliseconds(300))
            .EnviarAsync(Aviso(receptor.Url("avisos?demora=5")), CancellationToken.None);

        Assert.False(resultado.Exitoso);
        Assert.Null(resultado.CodigoHttp);
        Assert.Contains("no respondió", resultado.Error);
    }

    [Fact]
    public async Task Sin_destinos_locales_no_llama_al_receptor()
    {
        var cliente = Cliente(permitirLocales: false);
        var https = new UriBuilder(receptor.Direccion) { Scheme = "https", Path = "avisos" }.Uri.ToString();

        var porHttp = await cliente.EnviarAsync(Aviso(receptor.Url("avisos")), CancellationToken.None);
        var porHttps = await cliente.EnviarAsync(Aviso(https), CancellationToken.None);

        Assert.Contains("https", porHttp.Error);
        Assert.Contains("propia máquina", porHttps.Error);
        Assert.Empty(await receptor.RecibidosAsync());
    }

    [Theory]
    [InlineData("https://10.0.0.5/avisos", "red interna")]
    [InlineData("https://169.254.169.254/latest/meta-data", "red interna")]
    [InlineData("https://localhost/avisos", "propia máquina")]
    [InlineData("http://comercio.example/avisos", "https")]
    public async Task No_llama_a_destinos_internos(string url, string motivo)
    {
        var resultado = await Cliente(permitirLocales: false).EnviarAsync(Aviso(url), CancellationToken.None);

        Assert.False(resultado.Exitoso);
        Assert.Null(resultado.CodigoHttp);
        Assert.Contains("Destino no permitido", resultado.Error);
        Assert.Contains(motivo, resultado.Error);
    }

    private static ClienteAvisos Cliente(bool permitirLocales, TimeSpan? tiempoMaximo = null) =>
        new(new HttpClient(ClienteAvisos.CrearManejador(permitirLocales))
        {
            Timeout = tiempoMaximo ?? ClienteAvisos.TiempoMaximo
        });

    private static AvisoPendiente Aviso(string url) => new(
        Id: 1,
        AvisoId: Guid.NewGuid(),
        TipoEvento: TiposEvento.EnvioEstadoCambiado,
        Contenido: """{"EnvioId":101,"Estado":"EnDeposito"}""",
        Intento: 2,
        UrlDestino: url,
        CorrelacionId: "prueba-avisos",
        ClaveFirma: new string('A', 64),
        SuscripcionId:15);

    public async Task DisposeAsync() => await receptor.DisposeAsync();
}
