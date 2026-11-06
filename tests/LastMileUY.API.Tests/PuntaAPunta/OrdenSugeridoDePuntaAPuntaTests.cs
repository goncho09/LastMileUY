namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using static ApiDePuntaAPunta;

// El orden sugerido de las paradas por HTTP, con los usuarios reales: arranca del depósito del
// operador, deja al final lo que no tiene ubicación, no guarda nada, y respeta el aislamiento y los
// perfiles. También el depósito: quién lo carga y que se vea enseguida (el caché se invalida).
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class OrdenSugeridoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    // Sobre una misma latitud de Montevideo: cada paso de 0,01° de longitud son unos 0,9 km
    private static (decimal Latitud, decimal Longitud) En(int paso) => (-34.9m, -56.2m + paso * 0.01m);

    [Fact]
    public async Task Sugiere_desde_el_deposito_deja_al_final_lo_sin_ubicacion_y_no_guarda_nada()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        await GuardarDepositoAsync(operador, En(0));

        var (lejos, cerca, medio, sinUbicacion) = (
            await EnvioEnDepositoAsync(operador, En(3)),
            await EnvioEnDepositoAsync(operador, En(1)),
            await EnvioEnDepositoAsync(operador, En(2)),
            await EnvioEnDepositoAsync(operador, null));

        using var respuesta = await despachador.PostAsJsonAsync("api/rutas/sugerir-orden",
            new SugerirOrdenRequest { EnvioIds = [sinUbicacion.Id, lejos.Id, cerca.Id, medio.Id] });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var sugerencia = (await respuesta.Content.ReadFromJsonAsync<SugerenciaOrdenDto>())!;
        Assert.Equal([cerca.Id, medio.Id, lejos.Id, sinUbicacion.Id], sugerencia.EnvioIds);
        Assert.Equal([sinUbicacion.CodigoSeguimiento], sugerencia.SinUbicacion);
        Assert.True(sugerencia.DesdeDeposito);
        Assert.True(sugerencia.KmSugerido < sugerencia.KmActual);

        // Es solo una sugerencia: los envíos siguen en el depósito, sin ruta
        foreach (var id in new[] { lejos.Id, cerca.Id, medio.Id, sinUbicacion.Id })
        {
            var envio = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{id}"))!;
            Assert.Equal("EnDeposito", envio.Estado);
        }

        // Y la pantalla para armar rutas avisa cuál no tiene ubicación
        var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        Assert.True(opciones.Envios.Single(e => e.Id == cerca.Id).TieneUbicacion);
        Assert.False(opciones.Envios.Single(e => e.Id == sinUbicacion.Id).TieneUbicacion);
    }

    [Fact]
    public async Task Un_envio_nuevo_copia_la_ubicacion_de_la_direccion_del_destinatario()
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        // Las direcciones de prueba tienen ubicación (IdentitySeeder)
        var envio = await EnvioEnDepositoAsync(operador, ubicacion: default, cambiarUbicacion: false);

        var opciones = (await operador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        Assert.True(opciones.Envios.Single(e => e.Id == envio.Id).TieneUbicacion);
    }

    [Fact]
    public async Task Un_envio_de_otro_operador_no_existe_y_el_repartidor_no_puede_pedir_el_orden()
    {
        var operador1 = await api.IniciarSesionAsync(Operador1);
        var operador2 = await api.IniciarSesionAsync(Operador2);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);

        var propio = await EnvioEnDepositoAsync(operador1, En(1));
        var ajeno = await EnvioEnDepositoAsync(operador2, En(2), comercioId: api.IdComercio2);

        using (var conAjeno = await operador1.PostAsJsonAsync("api/rutas/sugerir-orden",
                   new SugerirOrdenRequest { EnvioIds = [propio.Id, ajeno.Id] }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, conAjeno.StatusCode);
        }

        using (var vacio = await operador1.PostAsJsonAsync("api/rutas/sugerir-orden", new SugerirOrdenRequest()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        }

        using var delRepartidor = await repartidor.PostAsJsonAsync("api/rutas/sugerir-orden",
            new SugerirOrdenRequest { EnvioIds = [propio.Id] });
        Assert.Equal(HttpStatusCode.Forbidden, delRepartidor.StatusCode);
    }

    [Fact]
    public async Task El_operador_carga_el_deposito_y_se_ve_enseguida()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);

        // Primero se lee, así queda en el caché
        _ = await operador.GetFromJsonAsync<OperadorActualDto>("api/operador");

        await GuardarDepositoAsync(operador, (-34.871234m, -56.165432m));

        var actual = (await operador.GetFromJsonAsync<OperadorActualDto>("api/operador"))!;
        Assert.Equal(new Coordenadas(-34.871234m, -56.165432m), actual.Deposito);

        // Solo una de las dos, o fuera de rango: no se acepta
        using (var incompleto = await operador.PutAsJsonAsync("api/operador/deposito",
                   new GuardarDepositoRequest { Latitud = -34.9m }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, incompleto.StatusCode);
        }

        using (var fueraDeRango = await operador.PutAsJsonAsync("api/operador/deposito",
                   new GuardarDepositoRequest { Latitud = -95m, Longitud = -56m }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, fueraDeRango.StatusCode);
        }

        // Es configuración del operador: el despachador no la cambia
        using (var delDespachador = await despachador.PutAsJsonAsync("api/operador/deposito",
                   new GuardarDepositoRequest { Latitud = -34.9m, Longitud = -56.1m }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, delDespachador.StatusCode);
        }

        // Las dos en null lo borra
        using (var borrar = await operador.PutAsJsonAsync("api/operador/deposito", new GuardarDepositoRequest()))
        {
            Assert.Equal(HttpStatusCode.NoContent, borrar.StatusCode);
        }

        Assert.Null((await operador.GetFromJsonAsync<OperadorActualDto>("api/operador"))!.Deposito);
    }

    // Aunque alguien escriba directo en la base: la ubicación va completa y dentro del mapa
    [Theory]
    [InlineData("""UPDATE "Envios" SET "DireccionEntrega_Latitud" = -34.9, "DireccionEntrega_Longitud" = NULL""")]
    [InlineData("""UPDATE "Direcciones" SET "Latitud" = 120, "Longitud" = -56""")]
    [InlineData("""UPDATE "Operadores" SET "DepositoLatitud" = NULL, "DepositoLongitud" = -56""")]
    public async Task La_base_no_acepta_una_ubicacion_a_medias_o_fuera_del_mapa(string sql)
    {
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => api.EjecutarAsync(sql));

        Assert.Equal("23514", error.SqlState); // check_violation
    }

    private static async Task GuardarDepositoAsync(HttpClient operador, (decimal Latitud, decimal Longitud) deposito)
    {
        using var respuesta = await operador.PutAsJsonAsync("api/operador/deposito",
            new GuardarDepositoRequest { Latitud = deposito.Latitud, Longitud = deposito.Longitud });

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
    }

    // Un envío nuevo pasado al depósito. La ubicación se le pone después con SQL (null: sin ubicación),
    // para que cada prueba tenga sus puntos; cambiarUbicacion: false deja la que copió de la dirección
    private async Task<EnvioResponse> EnvioEnDepositoAsync(
        HttpClient operador,
        (decimal Latitud, decimal Longitud)? ubicacion,
        int? comercioId = null,
        bool cambiarUbicacion = true)
    {
        var comercio = comercioId ?? api.IdComercio1;
        var (destinatario, direccion) = await api.DestinatarioDeAsync(comercio);

        using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = comercio,
            DestinatarioId = destinatario,
            DireccionId = direccion,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var envio = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!;

        using (var deposito = await operador.PatchAsJsonAsync($"api/envios/{envio.Id}/estado",
                   new CambiarEstadoRequest { Estado = "EnDeposito" }))
        {
            Assert.True(deposito.IsSuccessStatusCode);
        }

        if (cambiarUbicacion)
        {
            var (latitud, longitud) = ubicacion is { } u
                ? (u.Latitud.ToString(CultureInfo.InvariantCulture), u.Longitud.ToString(CultureInfo.InvariantCulture))
                : ("NULL", "NULL");

            await api.EjecutarAsync($"""
                UPDATE "Envios" SET "DireccionEntrega_Latitud" = {latitud}, "DireccionEntrega_Longitud" = {longitud}
                WHERE "Id" = {envio.Id}
                """);
        }

        return envio;
    }
}
