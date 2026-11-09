namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Mensajeria;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using static ApiDePuntaAPunta;

// Banderas de funcionalidad por operador por HTTP con los usuarios reales: solo el administrador las
// cambia, y apagar una corta esa funcionalidad para ese operador (y no para los demás).
// Las pruebas de la colección corren de a una: cada una deja las banderas como estaban.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class FuncionalidadesDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private const string Administrador = "admin@prueba.test";

    [Fact]
    public async Task El_administrador_ve_las_funcionalidades_de_cada_operador_y_los_demas_no()
    {
        var administrador = await api.IniciarSesionAsync(Administrador);

        var lista = (await administrador.GetFromJsonAsync<List<FuncionalidadesOperadorDto>>("api/admin/funcionalidades"))!;

        foreach (var operadorId in new[] { api.IdOperador1, api.IdOperador2 })
        {
            var operador = lista.Single(o => o.OperadorId == operadorId);
            Assert.Equal(Funcionalidades.Todas.Select(f => f.Clave), operador.Funcionalidades.Select(f => f.Clave));
            Assert.All(operador.Funcionalidades, f => Assert.True(f.Activa));
        }

        foreach (var email in new[] { Operador1, Despachador1, Comercio1 })
        {
            var cliente = await api.IniciarSesionAsync(email);

            using var ver = await cliente.GetAsync("api/admin/funcionalidades");
            Assert.Equal(HttpStatusCode.Forbidden, ver.StatusCode);

            using var cambiar = await cliente.PutAsJsonAsync(
                $"api/admin/operadores/{api.IdOperador1}/funcionalidades/{Funcionalidades.OrdenSugerido}",
                new CambiarFuncionalidadRequest { Activa = false });
            Assert.Equal(HttpStatusCode.Forbidden, cambiar.StatusCode);
        }
    }

    [Fact]
    public async Task Una_funcionalidad_o_un_operador_que_no_existen_se_rechazan()
    {
        var administrador = await api.IniciarSesionAsync(Administrador);

        using (var clave = await administrador.PutAsJsonAsync(
                   $"api/admin/operadores/{api.IdOperador1}/funcionalidades/teletransporte",
                   new CambiarFuncionalidadRequest { Activa = true }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, clave.StatusCode);
        }

        using var operador = await administrador.PutAsJsonAsync(
            $"api/admin/operadores/999999/funcionalidades/{Funcionalidades.OrdenSugerido}",
            new CambiarFuncionalidadRequest { Activa = false });
        Assert.Equal(HttpStatusCode.NotFound, operador.StatusCode);
    }

    [Fact]
    public async Task Sin_orden_sugerido_el_operador_no_lo_puede_pedir_y_los_demas_si()
    {
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var operador1 = await api.IniciarSesionAsync(Operador1);
        var operador2 = await api.IniciarSesionAsync(Operador2);
        var envio1 = await EnvioEnDepositoAsync(operador1, api.IdComercio1);
        var envio2 = await EnvioEnDepositoAsync(operador2, api.IdComercio2);

        await CambiarAsync(api.IdOperador1, Funcionalidades.OrdenSugerido, activa: false);
        try
        {
            // GET /api/operador ya no la trae (el caché se invalidó al cambiarla)
            var actual = (await despachador.GetFromJsonAsync<OperadorActualDto>("api/operador"))!;
            Assert.DoesNotContain(Funcionalidades.OrdenSugerido, actual.Funcionalidades!);
            Assert.False(actual.Tiene(Funcionalidades.OrdenSugerido));

            using var apagada = await despachador.PostAsJsonAsync("api/rutas/sugerir-orden",
                new SugerirOrdenRequest { EnvioIds = [envio1] });
            Assert.Equal(HttpStatusCode.Forbidden, apagada.StatusCode);
            Assert.Contains("Orden sugerido de paradas", await MensajeAsync(apagada));

            // El otro operador sigue igual
            using var otro = await operador2.PostAsJsonAsync("api/rutas/sugerir-orden",
                new SugerirOrdenRequest { EnvioIds = [envio2] });
            Assert.Equal(HttpStatusCode.OK, otro.StatusCode);
        }
        finally
        {
            await CambiarAsync(api.IdOperador1, Funcionalidades.OrdenSugerido, activa: true);
        }

        // Prendida otra vez, vuelve a andar
        using var prendida = await despachador.PostAsJsonAsync("api/rutas/sugerir-orden",
            new SugerirOrdenRequest { EnvioIds = [envio1] });
        Assert.Equal(HttpStatusCode.OK, prendida.StatusCode);
        Assert.Contains(Funcionalidades.OrdenSugerido,
            (await despachador.GetFromJsonAsync<OperadorActualDto>("api/operador"))!.Funcionalidades!);
    }

    [Fact]
    public async Task Sin_seguimiento_publico_el_codigo_del_operador_no_se_encuentra()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var envio = await EnvioEnDepositoAsync(operador, api.IdComercio1);
        var codigo = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envio}"))!.CodigoSeguimiento;
        var publico = api.ClienteSinSesion();

        // Se consulta recién con la bandera apagada: así no hay nada guardado en el caché del seguimiento
        await CambiarAsync(api.IdOperador1, Funcionalidades.SeguimientoPublico, activa: false);
        try
        {
            using var apagado = await publico.GetAsync($"api/seguimiento/{codigo}");
            Assert.Equal(HttpStatusCode.NotFound, apagado.StatusCode);
        }
        finally
        {
            await CambiarAsync(api.IdOperador1, Funcionalidades.SeguimientoPublico, activa: true);
        }

        using var prendido = await publico.GetAsync($"api/seguimiento/{codigo}");
        Assert.Equal(HttpStatusCode.OK, prendido.StatusCode);
    }

    [Fact]
    public async Task Sin_avisos_el_comercio_ve_sus_suscripciones_pero_no_crea_nuevas()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var pedido = new CrearSuscripcionAvisosRequest
        {
            TipoEvento = TiposEvento.EnvioAdmitido,
            UrlDestino = "https://comercio.example/avisos-banderas"
        };

        await CambiarAsync(api.IdOperador1, Funcionalidades.AvisosComercios, activa: false);
        try
        {
            using (var ver = await comercio.GetAsync("api/avisos/suscripciones"))
            {
                Assert.Equal(HttpStatusCode.OK, ver.StatusCode);
            }

            using var crear = await comercio.PostAsJsonAsync("api/avisos/suscripciones", pedido);
            Assert.Equal(HttpStatusCode.Forbidden, crear.StatusCode);
            Assert.Contains("Avisos a comercios", await MensajeAsync(crear));
        }
        finally
        {
            await CambiarAsync(api.IdOperador1, Funcionalidades.AvisosComercios, activa: true);
        }

        using var prendido = await comercio.PostAsJsonAsync("api/avisos/suscripciones", pedido);
        Assert.Equal(HttpStatusCode.Created, prendido.StatusCode);
    }

    // Varios cambios a la vez sobre una bandera que todavía no tiene fila (la primera vez se inserta):
    // ninguno puede terminar en un 500 por la clave duplicada. Las demás pruebas usan el operador 1:
    // para el 2 esta es la primera escritura. Queda prendida, que es su valor por defecto.
    [Fact]
    public async Task Varios_cambios_a_la_vez_de_una_bandera_nueva_no_fallan()
    {
        var administradores = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.IniciarSesionAsync(Administrador)));

        var respuestas = await Task.WhenAll(administradores.Select((cliente, i) => cliente.PutAsJsonAsync(
            $"api/admin/operadores/{api.IdOperador2}/funcionalidades/{Funcionalidades.SeguimientoPublico}",
            new CambiarFuncionalidadRequest { Activa = i % 2 == 0 })));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        await CambiarAsync(api.IdOperador2, Funcionalidades.SeguimientoPublico, activa: true);
        var lista = (await administradores[0].GetFromJsonAsync<List<FuncionalidadesOperadorDto>>("api/admin/funcionalidades"))!;
        Assert.True(lista.Single(o => o.OperadorId == api.IdOperador2)
            .Funcionalidades.Single(f => f.Clave == Funcionalidades.SeguimientoPublico).Activa);
    }

    private async Task CambiarAsync(int operadorId, string clave, bool activa)
    {
        var administrador = await api.IniciarSesionAsync(Administrador);

        using var respuesta = await administrador.PutAsJsonAsync(
            $"api/admin/operadores/{operadorId}/funcionalidades/{clave}",
            new CambiarFuncionalidadRequest { Activa = activa });

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
    }

    private static async Task<string> MensajeAsync(HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mensaje").GetString()!;

    private async Task<int> EnvioEnDepositoAsync(HttpClient operador, int comercioId)
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(comercioId);

        using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = comercioId,
            DestinatarioId = destinatario,
            DireccionId = direccion,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var id = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;

        using var deposito = await operador.PatchAsJsonAsync($"api/envios/{id}/estado",
            new CambiarEstadoRequest { Estado = "EnDeposito" });
        Assert.True(deposito.IsSuccessStatusCode);

        return id;
    }
}
