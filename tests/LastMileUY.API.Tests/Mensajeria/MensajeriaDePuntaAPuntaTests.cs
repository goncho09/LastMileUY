namespace LastMileUY.API.Tests.Mensajeria;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.API.Tests.PuntaAPunta;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Mensajeria;
using static LastMileUY.API.Tests.PuntaAPunta.ApiDePuntaAPunta;

// De punta a punta, con PostgreSQL y RabbitMQ reales: lo que se hace por HTTP llega como evento a
// RabbitMQ, con el identificador de correlación del pedido y sin datos personales.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class MensajeriaDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    [Fact]
    public async Task El_alta_llega_a_rabbitmq_con_la_correlacion_del_pedido()
    {
        var cliente = await api.IniciarSesionAsync(Operador1);

        var (envio, respuesta) = await CrearEnvioAsync(cliente, correlacion: "prueba-alta-1");

        Assert.Equal("prueba-alta-1", Correlacion(respuesta));

        var recibido = await api.EsperarEventoAsync(e => e.CodigoSeguimiento == envio.CodigoSeguimiento);
        Assert.Equal(TiposEvento.EnvioAdmitido, recibido.Evento.Tipo);
        Assert.Equal(envio.Id, recibido.Evento.EnvioId);
        Assert.Equal(api.IdOperador1, recibido.Evento.OperadorId);
        Assert.Equal("prueba-alta-1", recibido.Evento.CorrelacionId);
        Assert.Equal("prueba-alta-1", recibido.CorrelationId);
        Assert.Equal(recibido.Evento.EventoId.ToString(), recibido.MessageId);
    }

    [Fact]
    public async Task Sin_correlacion_la_api_genera_una_y_la_lleva_hasta_el_evento()
    {
        var cliente = await api.IniciarSesionAsync(Operador1);
        var (envio, _) = await CrearEnvioAsync(cliente);

        using var cambio = await cliente.PatchAsJsonAsync($"api/envios/{envio.Id}/estado",
            new CambiarEstadoRequest { Estado = "EnDeposito" });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var generada = Correlacion(cambio);

        var recibido = await api.EsperarEventoAsync(e =>
            e.CodigoSeguimiento == envio.CodigoSeguimiento && e.Tipo == TiposEvento.EnvioEstadoCambiado);
        Assert.False(string.IsNullOrWhiteSpace(generada));
        Assert.Equal(generada, recibido.CorrelationId);
        Assert.Equal("Admitido", recibido.Evento.EstadoAnterior);
        Assert.Equal("EnDeposito", recibido.Evento.Estado);
    }

    [Theory]
    [InlineData("con espacios")]
    [InlineData("<script>")]
    public async Task Una_correlacion_con_caracteres_raros_se_reemplaza(string recibida)
    {
        var cliente = api.ClienteSinSesion();
        using var pedido = new HttpRequestMessage(HttpMethod.Get, "api/seguimiento/NOEXISTE1234");
        pedido.Headers.TryAddWithoutValidation(TopologiaMensajeria.EncabezadoCorrelacion, recibida);

        using var respuesta = await cliente.SendAsync(pedido);

        Assert.NotEqual(recibida, Correlacion(respuesta));
        Assert.Matches("^[0-9a-f]{32}$", Correlacion(respuesta));
    }

    [Fact]
    public async Task El_evento_no_lleva_datos_personales_ni_notas_internas()
    {
        const string nota = "Portón verde, dejar con el vecino";
        var cliente = await api.IniciarSesionAsync(Operador1);
        var (envio, _) = await CrearEnvioAsync(cliente);
        var detalle = await cliente.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envio.Id}");

        using var cambio = await cliente.PatchAsJsonAsync($"api/envios/{envio.Id}/estado",
            new CambiarEstadoRequest { Estado = "EnDeposito", Descripcion = nota });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        var recibido = await api.EsperarEventoAsync(e =>
            e.CodigoSeguimiento == envio.CodigoSeguimiento && e.Tipo == TiposEvento.EnvioEstadoCambiado);
        Assert.DoesNotContain(nota, recibido.Cuerpo);
        Assert.DoesNotContain(detalle!.Destinatario.Nombre, recibido.Cuerpo);
        Assert.DoesNotContain(detalle.Direccion.Calle, recibido.Cuerpo);
    }

    private async Task<(EnvioResponse Envio, HttpResponseMessage Respuesta)> CrearEnvioAsync(
        HttpClient cliente, string? correlacion = null)
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var pedido = new HttpRequestMessage(HttpMethod.Post, "api/envios")
        {
            Content = JsonContent.Create(new CrearEnvioRequest
            {
                ComercioId = api.IdComercio1,
                DestinatarioId = destinatario,
                DireccionId = direccion
            })
        };

        if (correlacion is not null)
            pedido.Headers.Add(TopologiaMensajeria.EncabezadoCorrelacion, correlacion);

        var respuesta = await cliente.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        return ((await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!, respuesta);
    }

    private static string Correlacion(HttpResponseMessage respuesta) =>
        respuesta.Headers.GetValues(TopologiaMensajeria.EncabezadoCorrelacion).Single();
}
