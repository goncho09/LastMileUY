namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Destinatarios;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;
using static ApiDePuntaAPunta;
using DestinatarioDto = LastMileUY.Contracts.Destinatarios.DestinatarioDto;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class AmbientePruebasDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    [Fact]
    public async Task Lo_cargado_con_clave_de_prueba_no_se_mezcla_con_lo_de_verdad_en_ningun_sentido()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var produccion = ClienteConClave((await CrearClaveAsync(portal, "Tienda", AmbientesApi.Produccion)).Clave);
        var creada = await CrearClaveAsync(portal, "Tienda (pruebas)", AmbientesApi.Pruebas);
        var pruebas = ClienteConClave(creada.Clave);

        Assert.StartsWith("lmuy_test_", creada.Clave);
        Assert.True(creada.Datos.EsPrueba);
        var claves = (await portal.GetFromJsonAsync<List<ClaveApiDto>>("api/claves-api"))!;
        Assert.Equal(AmbientesApi.Pruebas, claves.Single(c => c.Id == creada.Datos.Id).Ambiente);

        var (destinatarioReal, direccionReal) = await api.DestinatarioDeAsync(api.IdComercio1);
        Assert.DoesNotContain((await pruebas.GetFromJsonAsync<List<DestinatarioDto>>("api/v1/destinatarios"))!,
            d => d.Id == destinatarioReal);

        using (var conReal = await pruebas.PostAsJsonAsync("api/v1/envios", Envio(destinatarioReal, direccionReal, null)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, conReal.StatusCode);
        }

        var destinatarioPrueba = await CrearDestinatarioAsync(pruebas);
        var referencia = "AMB-" + Guid.NewGuid().ToString("N")[..8];
        var envioPrueba = await CrearEnvioAsync(pruebas, destinatarioPrueba.Id, destinatarioPrueba.Direcciones[0].Id, referencia);

        var envioReal = await CrearEnvioAsync(produccion, destinatarioReal, direccionReal, referencia);

        var listadoPruebas = (await pruebas.GetFromJsonAsync<PaginaDto<EnvioResumenDto>>("api/v1/envios?tamanioPagina=100"))!;
        Assert.Contains(listadoPruebas.Items, e => e.Id == envioPrueba.Id);
        Assert.DoesNotContain(listadoPruebas.Items, e => e.Id == envioReal.Id);

        var listadoProduccion = (await produccion.GetFromJsonAsync<PaginaDto<EnvioResumenDto>>("api/v1/envios?tamanioPagina=100"))!;
        Assert.Contains(listadoProduccion.Items, e => e.Id == envioReal.Id);
        Assert.DoesNotContain(listadoProduccion.Items, e => e.Id == envioPrueba.Id);

        using (var desdeProduccion = await produccion.GetAsync($"api/v1/envios/{envioPrueba.Id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, desdeProduccion.StatusCode);
        }

        Assert.DoesNotContain((await produccion.GetFromJsonAsync<List<DestinatarioDto>>("api/v1/destinatarios"))!,
            d => d.Id == destinatarioPrueba.Id);

        using (var conDestinatarioDePrueba = await produccion.PostAsJsonAsync("api/v1/envios",
                   Envio(destinatarioPrueba.Id, destinatarioPrueba.Direcciones[0].Id, null)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, conDestinatarioDePrueba.StatusCode);
        }

        using (var direccionCruzada = await produccion.PostAsJsonAsync(
                   $"api/v1/destinatarios/{destinatarioPrueba.Id}/direcciones", Direccion()))
        {
            Assert.Equal(HttpStatusCode.NotFound, direccionCruzada.StatusCode);
        }

        using (var enElPortal = await portal.GetAsync($"api/envios/{envioPrueba.Id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, enElPortal.StatusCode);
        }

        Assert.DoesNotContain((await portal.GetFromJsonAsync<List<DestinatarioDto>>("api/destinatarios"))!,
            d => d.Id == destinatarioPrueba.Id);

        var operador = await api.IniciarSesionAsync(Operador1);
        using (var paraElOperador = await operador.GetAsync($"api/envios/{envioPrueba.Id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, paraElOperador.StatusCode);
        }

        Assert.DoesNotContain((await operador.GetFromJsonAsync<List<DestinatarioDto>>("api/destinatarios"))!,
            d => d.Id == destinatarioPrueba.Id);

        using var realDesdePruebas = await pruebas.GetAsync($"api/v1/envios/{envioReal.Id}");
        Assert.Equal(HttpStatusCode.NotFound, realDesdePruebas.StatusCode);

        using var direccionAReal = await pruebas.PostAsJsonAsync($"api/v1/destinatarios/{destinatarioReal}/direcciones", Direccion());
        Assert.Equal(HttpStatusCode.NotFound, direccionAReal.StatusCode);
    }

    [Fact]
    public async Task Las_claves_de_prueba_de_dos_comercios_no_se_ven_entre_si()
    {
        var pruebas1 = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Pruebas 1", AmbientesApi.Pruebas)).Clave);

        var pruebas2 = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio2, api.IdOperador1), "Pruebas 2", AmbientesApi.Pruebas)).Clave);

        var destinatario1 = await CrearDestinatarioAsync(pruebas1);
        var envio1 = await CrearEnvioAsync(pruebas1, destinatario1.Id, destinatario1.Direcciones[0].Id, null);

        using (var listado = await pruebas2.GetAsync("api/v1/destinatarios"))
        {
            Assert.Equal(HttpStatusCode.OK, listado.StatusCode);
            Assert.DoesNotContain((await listado.Content.ReadFromJsonAsync<List<DestinatarioDto>>())!, d => d.Id == destinatario1.Id);
        }

        using var ajeno = await pruebas2.GetAsync($"api/v1/envios/{envio1.Id}");
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
    }

    [Fact]
    public async Task En_pruebas_se_simula_el_recorrido_y_con_una_clave_de_produccion_no_se_puede()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var pruebas = ClienteConClave((await CrearClaveAsync(portal, "Simulador", AmbientesApi.Pruebas)).Clave);
        var produccion = ClienteConClave((await CrearClaveAsync(portal, "Real", AmbientesApi.Produccion)).Clave);

        var destinatario = await CrearDestinatarioAsync(pruebas);
        var envio = await CrearEnvioAsync(pruebas, destinatario.Id, destinatario.Direcciones[0].Id, null);

        var enTransito = await SimularAsync(pruebas, envio.Id, "EnTransito");
        Assert.Equal("EnTransito", enTransito.Estado);

        var entregado = await SimularAsync(pruebas, envio.Id, "Entregado");
        Assert.Equal("Entregado", entregado.Estado);
        Assert.All(entregado.Bultos, b => Assert.Equal("Entregado", b.Estado));
        Assert.Equal(4, entregado.Eventos.Count(e => e.Origen == "Simulación"));

        using (var haciaAtras = await pruebas.PostAsJsonAsync($"api/v1/pruebas/envios/{envio.Id}/simular",
                   new SimularEstadoRequest { Estado = "EnTransito" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, haciaAtras.StatusCode);
        }

        using (var inventado = await pruebas.PostAsJsonAsync($"api/v1/pruebas/envios/{envio.Id}/simular",
                   new SimularEstadoRequest { Estado = "Devuelto" }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, inventado.StatusCode);
        }

        var (destinatarioReal, direccionReal) = await api.DestinatarioDeAsync(api.IdComercio1);
        var real = await CrearEnvioAsync(produccion, destinatarioReal, direccionReal, null);

        using (var conProduccion = await produccion.PostAsJsonAsync($"api/v1/pruebas/envios/{real.Id}/simular",
                   new SimularEstadoRequest { Estado = "Entregado" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, conProduccion.StatusCode);
        }

        Assert.Equal("Admitido", (await produccion.GetFromJsonAsync<EnvioDetalleDto>($"api/v1/envios/{real.Id}"))!.Estado);

        using var realDesdePruebas = await pruebas.PostAsJsonAsync($"api/v1/pruebas/envios/{real.Id}/simular",
            new SimularEstadoRequest { Estado = "Entregado" });
        Assert.Equal(HttpStatusCode.NotFound, realDesdePruebas.StatusCode);
    }

    [Fact]
    public async Task Las_claves_de_prueba_tienen_la_mitad_del_tope_de_lotes()
    {
        var pruebas = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Tope pruebas", AmbientesApi.Pruebas)).Clave);

        for (var i = 0; i < 5; i++)
        {
            using var permitido = await pruebas.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest());
            Assert.Equal(HttpStatusCode.BadRequest, permitido.StatusCode);
        }

        using var frenado = await pruebas.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest());
        Assert.Equal(HttpStatusCode.TooManyRequests, frenado.StatusCode);
    }

    [Fact]
    public async Task El_operador_de_pruebas_no_aparece_para_elegir_ni_para_administrar()
    {
        await CrearClaveAsync(await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Para el listado", AmbientesApi.Pruebas);

        using var login = await api.ClienteSinSesion().PostAsJsonAsync("api/auth/login",
            new LoginRequest { Email = Comercio1, Password = Clave });
        Assert.Equal(HttpStatusCode.Conflict, login.StatusCode);
        var eleccion = (await login.Content.ReadFromJsonAsync<OperadorRequeridoResponse>())!;
        Assert.Equal([api.IdOperador1, api.IdOperador2], eleccion.Operadores.Select(o => o.Id).Order());

        var admin = await api.IniciarSesionAsync("admin@prueba.test");
        var operadores = await admin.GetStringAsync("api/admin/operadores");
        Assert.DoesNotContain("Ambiente de pruebas", operadores);

        var funcionalidades = await admin.GetStringAsync("api/admin/funcionalidades");
        Assert.DoesNotContain("Ambiente de pruebas", funcionalidades);

        var vinculos = await admin.GetStringAsync($"api/admin/comercios/{api.IdComercio1}/operadores");
        Assert.DoesNotContain("Ambiente de pruebas", vinculos);
    }

    [Fact]
    public async Task La_base_separa_los_destinatarios_por_ambiente_aunque_no_se_pase_por_EF()
    {
        var pruebas = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "RLS", AmbientesApi.Pruebas)).Clave);
        var destinatario = await CrearDestinatarioAsync(pruebas);

        var comercio = $"SELECT set_config('app.comercio_id', '{api.IdComercio1}', false)";
        var esteDestinatario = $"SELECT count(*) FROM \"Destinatarios\" WHERE \"Id\" = {destinatario.Id}";

        Assert.Equal(0, await api.ContarComoAplicacionAsync(comercio, esteDestinatario));
        Assert.True(await api.ContarComoAplicacionAsync(comercio, "SELECT count(*) FROM \"Destinatarios\"") > 0);

        var enPruebas = comercio + "; SELECT set_config('app.ambiente_pruebas', '1', false)";
        Assert.Equal(1, await api.ContarComoAplicacionAsync(enPruebas, esteDestinatario));
        Assert.Equal(0, await api.ContarComoAplicacionAsync(enPruebas,
            "SELECT count(*) FROM \"Destinatarios\" WHERE NOT \"EsPrueba\""));

        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => api.ContarComoAplicacionAsync(comercio, $"""
            WITH nuevo AS (INSERT INTO "Destinatarios"("ComercioId", "Nombre", "Apellido", "FechaCreacion", "EsPrueba")
                           VALUES ({api.IdComercio1}, 'X', 'Y', now(), true) RETURNING 1)
            SELECT count(*) FROM nuevo
            """));
    }

    private static async Task<ClaveApiCreadaDto> CrearClaveAsync(HttpClient portal, string nombre, string ambiente)
    {
        using var respuesta = await portal.PostAsJsonAsync("api/claves-api",
            new CrearClaveApiRequest { Nombre = nombre, Ambiente = ambiente });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ClaveApiCreadaDto>())!;
    }

    private HttpClient ClienteConClave(string clave)
    {
        var cliente = api.ClienteSinSesion();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", clave);
        return cliente;
    }

    private static async Task<DestinatarioDto> CrearDestinatarioAsync(HttpClient sistema)
    {
        using var respuesta = await sistema.PostAsJsonAsync("api/v1/destinatarios", new CrearDestinatarioRequest
        {
            Nombre = "Prueba",
            Apellido = "Integración",
            Telefono = "099 000 000",
            Direccion = Direccion()
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<DestinatarioDto>())!;
    }

    private static async Task<EnvioResponse> CrearEnvioAsync(HttpClient sistema, int destinatario, int direccion, string? referencia)
    {
        using var respuesta = await sistema.PostAsJsonAsync("api/v1/envios", Envio(destinatario, direccion, referencia));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!;
    }

    private static async Task<EnvioDetalleDto> SimularAsync(HttpClient pruebas, int envioId, string estado)
    {
        using var respuesta = await pruebas.PostAsJsonAsync($"api/v1/pruebas/envios/{envioId}/simular",
            new SimularEstadoRequest { Estado = estado });
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EnvioDetalleDto>())!;
    }

    private static CrearDireccionRequest Direccion() => new()
    {
        Calle = "Rambla",
        Numero = "100",
        Ciudad = "Montevideo",
        Departamento = "Montevideo"
    };

    private static EnvioApiRequest Envio(int destinatario, int direccion, string? referencia) => new()
    {
        ReferenciaExterna = referencia,
        DestinatarioId = destinatario,
        DireccionId = direccion,
        Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
    };
}
