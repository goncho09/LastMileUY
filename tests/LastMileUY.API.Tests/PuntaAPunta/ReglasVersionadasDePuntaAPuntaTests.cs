using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Configuracion;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ReglasVersionadasDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private async Task<int> CrearEnvioAsync(HttpClient comercio)
    {
        var (destinatarioId, direccionId) = await api.DestinatarioDeAsync(api.IdComercio1);

        using var respuesta = await comercio.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatarioId,
            DireccionId = direccionId,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;
    }

    private static async Task<ReglaAplicadaDto> ReglaDelEnvioAsync(HttpClient cliente, int envioId) =>
        (await cliente.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!.Regla!;

    [Fact]
    public async Task Cambiar_una_regla_en_uso_crea_una_version_nueva_y_los_envios_viejos_conservan_la_suya()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var reglas = (await operador.GetFromJsonAsync<List<ReglaOperativaConfiguracionDto>>("api/operador/reglas-operativas"))!;
        var original = reglas.Single(r => r.Activa && r.Modalidad == "Estandar");
        var nombre = $"Versionada {Guid.NewGuid():N}"[..20];

        try
        {
            using var creada = await operador.PostAsJsonAsync("api/operador/reglas-operativas",
                new GuardarReglaOperativaRequest(nombre, null, "Estandar", 4, 24, 72, 48, false, false, false));
            Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
            var version1 = (await creada.Content.ReadFromJsonAsync<ReglaOperativaConfiguracionDto>())!;

            var envioViejo = await CrearEnvioAsync(comercio);
            Assert.Equal(4, (await ReglaDelEnvioAsync(operador, envioViejo)).MaximoIntentosEntrega);

            using var editada = await operador.PutAsJsonAsync($"api/operador/reglas-operativas/{version1.Id}",
                new GuardarReglaOperativaRequest(nombre, null, "Estandar", 1, 24, 24, 48, true, false, false));
            Assert.Equal(HttpStatusCode.NoContent, editada.StatusCode);

            var despues = (await operador.GetFromJsonAsync<List<ReglaOperativaConfiguracionDto>>("api/operador/reglas-operativas"))!;
            var version2 = Assert.Single(despues, r => r.Activa && r.Modalidad == "Estandar");
            var anterior = despues.Single(r => r.Id == version1.Id);

            Assert.NotEqual(version1.Id, version2.Id);
            Assert.Equal(1, version2.MaximoIntentosEntrega);
            Assert.False(anterior.Activa);
            Assert.Equal(4, anterior.MaximoIntentosEntrega);
            Assert.Equal(1, anterior.EnviosAsociados);

            var reglaVieja = await ReglaDelEnvioAsync(operador, envioViejo);
            Assert.Equal(4, reglaVieja.MaximoIntentosEntrega);
            Assert.False(reglaVieja.RequiereFirma);

            var envioNuevo = await CrearEnvioAsync(comercio);
            var reglaNueva = await ReglaDelEnvioAsync(operador, envioNuevo);
            Assert.Equal(1, reglaNueva.MaximoIntentosEntrega);
            Assert.True(reglaNueva.RequiereFirma);

        }
        finally
        {
            using var restaurar = await operador.PutAsJsonAsync($"api/operador/reglas-operativas/{original.Id}/activa", new CambiarActivoRequest(true));
            Assert.Equal(HttpStatusCode.NoContent, restaurar.StatusCode);
        }
    }
}
