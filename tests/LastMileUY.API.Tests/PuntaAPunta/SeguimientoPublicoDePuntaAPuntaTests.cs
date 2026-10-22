namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Seguimiento;
using static ApiDePuntaAPunta;

// Seguimiento público por HTTP: un envío creado y movido por el operador se consulta sin sesión
// con su código, y la respuesta no trae datos personales ni notas internas.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class SeguimientoPublicoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private const string NotaInterna = "Llamar antes, el portero no recibe";

    [Fact]
    public async Task Sin_sesion_se_ve_el_estado_y_el_historial_del_envio()
    {
        var codigo = (await CrearYMoverEnvioAsync()).CodigoSeguimiento;

        var seguimiento = await api.ClienteSinSesion()
            .GetFromJsonAsync<SeguimientoPublicoDto>($"api/seguimiento/{codigo}");

        Assert.NotNull(seguimiento);
        Assert.Equal("EnDeposito", seguimiento.Estado);
        Assert.Equal(["Admitido", "EnDeposito"], seguimiento.Eventos.Select(e => e.Estado));
        Assert.Equal("Operador Prueba 1", seguimiento.Operador.Nombre);
    }

    [Fact]
    public async Task La_respuesta_no_trae_datos_personales_ni_notas_internas()
    {
        var envio = await CrearYMoverEnvioAsync();
        var cliente = await api.IniciarSesionAsync(Operador1);
        var completo = await cliente.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envio.Id}");

        var json = await api.ClienteSinSesion().GetStringAsync($"api/seguimiento/{envio.CodigoSeguimiento}");

        Assert.DoesNotContain(NotaInterna, json);
        Assert.DoesNotContain(completo!.Destinatario.Nombre, json);
        Assert.DoesNotContain(completo.Direccion.Calle, json);
        Assert.DoesNotContain($"\"id\"", json);
    }

    [Theory]
    [InlineData("NOEXISTE1234")]
    [InlineData("con-guiones")]
    public async Task Un_codigo_que_no_existe_da_404(string codigo)
    {
        using var respuesta = await api.ClienteSinSesion().GetAsync($"api/seguimiento/{codigo}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // Envío del operador 1 que pasa a depósito con una nota interna
    private async Task<EnvioResponse> CrearYMoverEnvioAsync()
    {
        var cliente = await api.IniciarSesionAsync(Operador1);
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);

        using var alta = await cliente.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatario,
            DireccionId = direccion
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var envio = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!;

        using var cambio = await cliente.PatchAsJsonAsync($"api/envios/{envio.Id}/estado",
            new CambiarEstadoRequest { Estado = "EnDeposito", Descripcion = NotaInterna });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        return envio;
    }
}
