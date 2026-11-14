using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Deposito;
using LastMileUY.Contracts.Envios;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class RecepcionDepositoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private async Task<EnvioResponse> EnvioConDosBultosAsync()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var (destinatarioId, direccionId) = await api.DestinatarioDeAsync(api.IdComercio1);

        using var respuesta = await comercio.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatarioId,
            DireccionId = direccionId,
            Bultos =
            [
                new CrearBultoRequest { PesoKg = 2m, LargoCm = 30, AnchoCm = 20, AltoCm = 10 },
                new CrearBultoRequest { PesoKg = 5m, LargoCm = 40, AnchoCm = 30, AltoCm = 20 }
            ]
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!;
    }

    private static async Task<RecepcionDto> RecibirAsync(HttpClient cliente, RegistrarRecepcionRequest pedido)
    {
        using var respuesta = await cliente.PostAsJsonAsync("api/deposito/recepciones", pedido);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<RecepcionDto>())!;
    }

    private static async Task<string> EstadoAsync(HttpClient cliente, int envioId) =>
        (await cliente.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!.Estado;

    [Fact]
    public async Task Un_bulto_faltante_deja_el_envio_esperando_y_al_completarlo_entra_al_deposito()
    {
        var deposito = await api.IniciarSesionAsync(Despachador1);
        var envio = await EnvioConDosBultosAsync();
        var bulto1 = $"{envio.CodigoSeguimiento}-1";
        var bulto2 = $"{envio.CodigoSeguimiento}-2";

        var porRecibir = (await deposito.GetFromJsonAsync<List<EnvioPorRecibirDto>>("api/deposito/por-recibir"))!;
        Assert.Contains(porRecibir, e => e.Id == envio.Id && e.CantidadBultos == 2);

        var encontrado = (await deposito.GetFromJsonAsync<EnvioParaRecibirDto>($"api/deposito/recepcion/{bulto2.ToLowerInvariant()}"))!;
        Assert.Equal(envio.Id, encontrado.Id);
        Assert.True(encontrado.SePuedeRecibir);

        var incompleta = await RecibirAsync(deposito, new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest(bulto1, EstadosBultoRecibido.Recibido),
            new BultoRecibidoRequest(bulto2, EstadosBultoRecibido.Faltante)
        ]));

        Assert.False(incompleta.Aceptada);
        Assert.True(incompleta.Pendiente);
        Assert.Equal("Admitido", await EstadoAsync(deposito, envio.Id));
        Assert.Contains((await deposito.GetFromJsonAsync<List<EnvioPorRecibirDto>>("api/deposito/por-recibir"))!,
            e => e.Id == envio.Id && e.TuvoRecepcionIncompleta);

        var completa = await RecibirAsync(deposito, new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest(bulto1, EstadosBultoRecibido.Recibido, 2.05m),
            new BultoRecibidoRequest(bulto2, EstadosBultoRecibido.Recibido, 7.5m)
        ], "Llegó en la segunda camioneta"));

        Assert.True(completa.Aceptada);
        Assert.True(completa.Pendiente);
        Assert.False(completa.Bultos.Single(b => b.Codigo == bulto1).PesoDistinto);
        Assert.True(completa.Bultos.Single(b => b.Codigo == bulto2).PesoDistinto);
        Assert.Equal("EnDeposito", await EstadoAsync(deposito, envio.Id));

        var pendientes = (await deposito.GetFromJsonAsync<List<RecepcionDto>>("api/deposito/discrepancias"))!;
        Assert.DoesNotContain(pendientes, r => r.Id == incompleta.Id);
        Assert.Contains(pendientes, r => r.Id == completa.Id);

        using (var resolver = await deposito.PostAsJsonAsync($"api/deposito/discrepancias/{completa.Id}/resolver",
                   new ResolverDiscrepanciaRequest("Se le avisó al comercio de la diferencia de peso.")))
        {
            Assert.Equal(HttpStatusCode.NoContent, resolver.StatusCode);
        }

        using (var deNuevo = await deposito.PostAsJsonAsync($"api/deposito/discrepancias/{completa.Id}/resolver",
                   new ResolverDiscrepanciaRequest("Otra vez")))
        {
            Assert.Equal(HttpStatusCode.Conflict, deNuevo.StatusCode);
        }

        using var otraRecepcion = await deposito.PostAsJsonAsync("api/deposito/recepciones", new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest(bulto1, EstadosBultoRecibido.Recibido),
            new BultoRecibidoRequest(bulto2, EstadosBultoRecibido.Recibido)
        ]));
        Assert.Equal(HttpStatusCode.Conflict, otraRecepcion.StatusCode);

        var historial = (await deposito.GetFromJsonAsync<List<RecepcionDto>>("api/deposito/discrepancias?pendientes=false"))!;
        Assert.Contains(historial, r => r.Id == incompleta.Id && r.Resolucion == "Se completó en una recepción posterior.");
    }

    [Fact]
    public async Task El_operario_de_deposito_recibe_pero_no_planifica_rutas()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var email = $"deposito.{Guid.NewGuid():N}@prueba.test";

        using (var alta = await operador.PostAsJsonAsync("api/usuarios",
                   new LastMileUY.Contracts.Usuarios.CrearUsuarioRequest(email, "Bruno", "Silva", Clave, "OperarioDeposito", null, null)))
        {
            Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        }

        var operario = await api.IniciarSesionAsync(email);
        var envio = await EnvioConDosBultosAsync();

        var recepcion = await RecibirAsync(operario, new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-1", EstadosBultoRecibido.Recibido),
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-2", EstadosBultoRecibido.Recibido)
        ]));

        using var rutas = await operario.GetAsync("api/rutas/opciones");

        Assert.True(recepcion.Aceptada);
        Assert.False(recepcion.TieneDiscrepancias);
        Assert.Equal(HttpStatusCode.Forbidden, rutas.StatusCode);
    }

    [Fact]
    public async Task Un_bulto_danado_entra_al_deposito_pero_queda_como_discrepancia()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var envio = await EnvioConDosBultosAsync();

        var recepcion = await RecibirAsync(operador, new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-1", EstadosBultoRecibido.Recibido),
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-2", EstadosBultoRecibido.Danado)
        ], "Caja mojada"));

        Assert.True(recepcion.Aceptada);
        Assert.True(recepcion.Pendiente);
        Assert.Equal("EnDeposito", await EstadoAsync(operador, envio.Id));
    }

    [Fact]
    public async Task Hay_que_informar_cada_bulto_una_vez_y_con_un_estado_valido()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var envio = await EnvioConDosBultosAsync();
        var otro = await EnvioConDosBultosAsync();
        var bulto1 = $"{envio.CodigoSeguimiento}-1";
        var bulto2 = $"{envio.CodigoSeguimiento}-2";

        RegistrarRecepcionRequest[] invalidos =
        [
            new(envio.Id, [new(bulto1, EstadosBultoRecibido.Recibido)]),
            new(envio.Id, [new(bulto1, EstadosBultoRecibido.Recibido), new(bulto1, EstadosBultoRecibido.Recibido)]),
            new(envio.Id, [new(bulto1, EstadosBultoRecibido.Recibido), new($"{otro.CodigoSeguimiento}-1", EstadosBultoRecibido.Recibido)]),
            new(envio.Id, [new(bulto1, EstadosBultoRecibido.Recibido), new(bulto2, "Perdido")]),
            new(envio.Id, [new(bulto1, EstadosBultoRecibido.Recibido), new(bulto2, EstadosBultoRecibido.Faltante, 3m)]),
            new(envio.Id, [new(bulto1, EstadosBultoRecibido.Recibido, -1m), new(bulto2, EstadosBultoRecibido.Recibido)])
        ];

        foreach (var pedido in invalidos)
        {
            using var respuesta = await operador.PostAsJsonAsync("api/deposito/recepciones", pedido);
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        }

        Assert.Equal("Admitido", await EstadoAsync(operador, envio.Id));
    }

    [Fact]
    public async Task Otro_operador_no_ve_el_envio_y_el_comercio_no_entra_al_deposito()
    {
        var envio = await EnvioConDosBultosAsync();
        var otroOperador = await api.IniciarSesionAsync(Operador2);
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);

        using var buscar = await otroOperador.GetAsync($"api/deposito/recepcion/{envio.CodigoSeguimiento}");
        using var recibir = await otroOperador.PostAsJsonAsync("api/deposito/recepciones", new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-1", EstadosBultoRecibido.Recibido),
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-2", EstadosBultoRecibido.Recibido)
        ]));
        var ajenos = (await otroOperador.GetFromJsonAsync<List<EnvioPorRecibirDto>>("api/deposito/por-recibir"))!;
        using var comoComercio = await comercio.GetAsync("api/deposito/por-recibir");

        Assert.Equal(HttpStatusCode.NotFound, buscar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, recibir.StatusCode);
        Assert.DoesNotContain(ajenos, e => e.Id == envio.Id);
        Assert.Equal(HttpStatusCode.Forbidden, comoComercio.StatusCode);

        await RecibirAsync(await api.IniciarSesionAsync(Operador1), new RegistrarRecepcionRequest(envio.Id,
        [
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-1", EstadosBultoRecibido.Recibido),
            new BultoRecibidoRequest($"{envio.CodigoSeguimiento}-2", EstadosBultoRecibido.Danado)
        ]));

        var deEsteEnvio = $"""
            SELECT count(*) FROM "RecepcionesDeposito" r JOIN "RecepcionesBulto" b ON b."RecepcionDepositoId" = r."Id"
            WHERE r."EnvioId" = {envio.Id}
            """;
        Assert.Equal(2, await api.ContarComoAplicacionAsync($"SELECT set_config('app.operador_id', '{api.IdOperador1}', false)", deEsteEnvio));
        Assert.Equal(0, await api.ContarComoAplicacionAsync($"SELECT set_config('app.operador_id', '{api.IdOperador2}', false)", deEsteEnvio));
        Assert.Equal(0, await api.ContarComoAplicacionAsync("", "SELECT count(*) FROM \"RecepcionesBulto\""));
    }
}
