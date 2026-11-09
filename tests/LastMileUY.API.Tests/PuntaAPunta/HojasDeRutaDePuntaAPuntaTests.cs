namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using static ApiDePuntaAPunta;

// Hojas de ruta por HTTP con los usuarios reales: el despachador arma y despacha la ruta,
// y solo el repartidor asignado la descarga (y solo una vez despachada).
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class HojasDeRutaDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    [Fact]
    public async Task El_despachador_arma_y_despacha_la_ruta_y_solo_su_repartidor_la_descarga()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var repartidorDeOtroOperador = await api.IniciarSesionAsync(Repartidor2);

        // Dos envíos en depósito
        var envios = new[] { await CrearEnvioEnDepositoAsync(operador), await CrearEnvioEnDepositoAsync(operador) };

        // El despachador ve qué puede usar: el repartidor del operador 1, sus vehículos y los envíos
        var opciones = (await despachador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        var repartidor1 = Assert.Single(opciones.Repartidores);
        Assert.NotEmpty(opciones.Vehiculos);
        Assert.All(envios, id => Assert.Contains(opciones.Envios, e => e.Id == id));

        using var alta = await despachador.PostAsJsonAsync("api/rutas", new CrearRutaRequest
        {
            Nombre = "Centro mañana",
            Fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            RepartidorId = repartidor1.Id,
            VehiculoId = opciones.Vehiculos[0].Id,
            EnvioIds = [envios[1], envios[0]]
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var ruta = (await alta.Content.ReadFromJsonAsync<RutaDetalleDto>())!;
        Assert.Equal([envios[1], envios[0]], ruta.Paradas.Select(p => p.EnvioId));

        // Planificada: el repartidor todavía no la puede descargar
        using (var antes = await repartidor.GetAsync($"api/rutas/{ruta.Id}/hoja"))
        {
            Assert.Equal(HttpStatusCode.Conflict, antes.StatusCode);
        }

        using (var despacho = await despachador.PostAsync($"api/rutas/{ruta.Id}/despachar", null))
        {
            Assert.Equal(HttpStatusCode.OK, despacho.StatusCode);
        }

        // El repartidor asignado la ve en sus rutas y descarga la hoja
        var mias = (await repartidor.GetFromJsonAsync<List<RutaResumenDto>>("api/rutas/mias"))!;
        Assert.Contains(mias, r => r.Id == ruta.Id);

        var hoja = (await repartidor.GetFromJsonAsync<RutaDetalleDto>($"api/rutas/{ruta.Id}/hoja"))!;
        Assert.Equal("Despachada", hoja.Estado);
        Assert.All(hoja.Paradas, p => Assert.Equal("EnTransito", p.EstadoEnvio));

        // El repartidor de otro operador ni siquiera la encuentra
        using (var ajena = await repartidorDeOtroOperador.GetAsync($"api/rutas/{ruta.Id}/hoja"))
        {
            Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        }

        // Y en el Backoffice los envíos aparecen en tránsito, con el despacho en el historial
        var detalle = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envios[0]}"))!;
        Assert.Equal("EnTransito", detalle.Estado);
        Assert.Contains(detalle.Eventos, e => e.Descripcion == "Salió en la ruta \"Centro mañana\".");
    }

    [Theory]
    [InlineData(Comercio1, "GET", "api/rutas")]
    [InlineData(Repartidor1, "GET", "api/rutas")]
    [InlineData(Repartidor1, "GET", "api/rutas/opciones")]
    [InlineData(Repartidor1, "POST", "api/rutas/1/despachar")]
    [InlineData(Operador1, "GET", "api/rutas/mias")]
    [InlineData(Despachador1, "GET", "api/rutas/1/hoja")]
    public async Task Cada_perfil_usa_solo_lo_suyo(string email, string metodo, string url)
    {
        var cliente = await api.IniciarSesionAsync(email);

        using var respuesta = await cliente.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), url));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // Sin bultos no hay nada que entregar: el envío no se podría asignar a ninguna ruta. Ni con la
    // lista vacía ni sin mandarla; tampoco desde el portal del comercio
    [Theory]
    [InlineData(Operador1, """{ "Bultos": [] }""")]
    [InlineData(Operador1, "{}")]
    [InlineData(Comercio1, """{ "Bultos": null }""")]
    public async Task No_se_puede_crear_un_envio_sin_bultos(string email, string bultos)
    {
        var cliente = await api.IniciarSesionAsync(email);
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var cuerpo = System.Text.Json.Nodes.JsonNode.Parse(bultos)!.AsObject();
        cuerpo["ComercioId"] = api.IdComercio1;
        cuerpo["DestinatarioId"] = destinatario;
        cuerpo["DireccionId"] = direccion;

        using var alta = await cliente.PostAsJsonAsync("api/envios", cuerpo);

        Assert.Equal(HttpStatusCode.BadRequest, alta.StatusCode);
        Assert.Contains("al menos un bulto", await alta.Content.ReadAsStringAsync());
    }

    // Un envío que quedó sin bultos (de antes de exigirlos) no se puede asignar a una ruta: la
    // pantalla para armarlas no lo tiene que ofrecer
    [Fact]
    public async Task Un_envio_sin_bultos_no_se_ofrece_para_armar_una_ruta()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var conBultos = await CrearEnvioEnDepositoAsync(operador);
        var sinBultos = await CrearEnvioEnDepositoAsync(operador);
        await api.EjecutarAsync($"""DELETE FROM "Bultos" WHERE "EnvioId" = {sinBultos}""");

        var opciones = (await operador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;

        Assert.Contains(opciones.Envios, e => e.Id == conBultos);
        Assert.DoesNotContain(opciones.Envios, e => e.Id == sinBultos);
    }

    // Dos despachadores arman a la vez rutas distintas (envíos y vehículos distintos) para el mismo
    // repartidor y el mismo día: una sola puede quedar abierta. Se repite en varios días porque la
    // carrera depende del momento justo en que llega cada pedido.
    [Fact]
    public async Task Dos_rutas_a_la_vez_para_el_mismo_repartidor_y_dia_solo_se_guarda_una()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachadorA = await api.IniciarSesionAsync(Despachador1);
        var despachadorB = await api.IniciarSesionAsync(Operador1);
        var opciones = (await despachadorA.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        var repartidor = opciones.Repartidores[0].Id;
        Assert.True(opciones.Vehiculos.Count >= 2);

        for (var dia = 300; dia < 306; dia++)
        {
            var fecha = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(dia);
            var envioA = await CrearEnvioEnDepositoAsync(operador);
            var envioB = await CrearEnvioEnDepositoAsync(operador);

            var respuestas = await Task.WhenAll(
                despachadorA.PostAsJsonAsync("api/rutas", Ruta($"Carrera A {dia}", fecha, repartidor, opciones.Vehiculos[0].Id, envioA)),
                despachadorB.PostAsJsonAsync("api/rutas", Ruta($"Carrera B {dia}", fecha, repartidor, opciones.Vehiculos[1].Id, envioB)));

            var codigos = respuestas.Select(r => r.StatusCode).Order().ToList();
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], codigos);

            foreach (var respuesta in respuestas)
                respuesta.Dispose();
        }
    }

    private static CrearRutaRequest Ruta(string nombre, DateOnly fecha, int repartidor, int vehiculo, int envio) => new()
    {
        Nombre = nombre,
        Fecha = fecha,
        RepartidorId = repartidor,
        VehiculoId = vehiculo,
        EnvioIds = [envio]
    };

    private async Task<int> CrearEnvioEnDepositoAsync(HttpClient operador)
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);

        using var alta = await operador.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatario,
            DireccionId = direccion,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10, Descripcion = "Bulto de prueba" }]
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var id = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;

        using var deposito = await operador.PatchAsJsonAsync($"api/envios/{id}/estado",
            new CambiarEstadoRequest { Estado = "EnDeposito" });
        Assert.True(deposito.IsSuccessStatusCode);

        return id;
    }
}
