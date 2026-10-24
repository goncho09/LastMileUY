using LastMileUY.Contracts.Rutas;

namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Auth;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using static ApiDePuntaAPunta;

// Tarjeta "Tests" del sprint, probada por HTTP con los usuarios reales de cada perfil:
// 1. permisos por perfil; 2. aislamiento entre operadores y entre comercios;
// 3. reglas distintas para los dos operadores; 4. alta desde el portal -> visible en el Backoffice;
// 5. cambios de estado, rechazo de transiciones e historial.
// Las URL son las mismas que usan el Backoffice (EnviosApiHttp) y el portal del comercio.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class RecorridoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    // --- 1. Permisos con usuarios reales de cada perfil ---

    [Theory]
    [InlineData(Operador1, "Operador")]
    [InlineData(Despachador1, "Despachador")]
    [InlineData(Comercio1, "Comercio")]
    [InlineData(Repartidor1, "Repartidor")]
    public async Task Cada_usuario_entra_con_su_perfil(string email, string perfil)
    {
        var cliente = await api.IniciarSesionAsync(email);

        var yo = await cliente.GetFromJsonAsync<UsuarioAutenticadoResponse>("api/auth/me");

        Assert.Equal(email, yo?.Email);
        Assert.Equal([perfil], yo?.Roles);
    }

    [Theory]
    [InlineData(Operador1, HttpStatusCode.OK)]
    [InlineData(Despachador1, HttpStatusCode.OK)]
    [InlineData(Comercio1, HttpStatusCode.Forbidden)]
    [InlineData(Repartidor1, HttpStatusCode.Forbidden)]
    public async Task Solo_el_personal_del_operador_cambia_estados(string email, HttpStatusCode esperado)
    {
        var id = await CrearEnvioComoOperador1Async();
        var cliente = await api.IniciarSesionAsync(email);

        using var respuesta = await CambiarEstadoAsync(cliente, id, "EnDeposito");

        Assert.Equal(esperado, respuesta.StatusCode);
    }

    [Theory]
    [InlineData("GET", "api/envios")]
    [InlineData("GET", "api/envios/1")]
    [InlineData("GET", "api/operador")]
    [InlineData("PATCH", "api/envios/1/estado")]
    [InlineData("POST", "api/envios")]
    public async Task Sin_sesion_no_se_accede_a_nada(string metodo, string url)
    {
        using var pedido = new HttpRequestMessage(new HttpMethod(metodo), url)
        {
            Content = metodo == "GET" ? null : JsonContent.Create(new { })
        };

        using var respuesta = await api.ClienteSinSesion().SendAsync(pedido);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_contrasena_incorrecta_no_inicia_sesion()
    {
        using var respuesta = await api.ClienteSinSesion().PostAsJsonAsync("api/auth/login",
            new LoginRequest { Email = Operador1, Password = "Incorrecta123" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // --- 2. Aislamiento entre operadores y entre comercios ---

    [Fact]
    public async Task Un_operador_no_ve_ni_modifica_los_envios_de_otro()
    {
        var id = await CrearEnvioComoOperador1Async();
        var operador2 = await api.IniciarSesionAsync(Operador2);

        using var detalle = await operador2.GetAsync($"api/envios/{id}");
        using var cambio = await CambiarEstadoAsync(operador2, id, "EnDeposito");
        var listado = await ListarAsync(operador2, "pagina=1&tamanioPagina=100");

        Assert.Equal(HttpStatusCode.NotFound, detalle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cambio.StatusCode);
        Assert.DoesNotContain(listado.Items, e => e.Id == id);

        // El dueño sí lo ve
        using var delDueno = await (await api.IniciarSesionAsync(Operador1)).GetAsync($"api/envios/{id}");
        Assert.Equal(HttpStatusCode.OK, delDueno.StatusCode);
    }

    [Fact]
    public async Task Un_comercio_no_ve_los_envios_de_otro_comercio_aunque_trabajen_con_el_mismo_operador()
    {
        // Los dos comercios trabajan con el operador 1; el envío es del comercio 1
        var id = await CrearEnvioComoOperador1Async();
        var comercio1 = await api.IniciarSesionAsync(Comercio1);
        var comercio2 = await api.IniciarSesionAsync(Comercio2, api.IdOperador1);

        using var propio = await comercio1.GetAsync($"api/envios/{id}");
        using var ajeno = await comercio2.GetAsync($"api/envios/{id}");
        var listadoAjeno = await ListarAsync(comercio2, "pagina=1&tamanioPagina=100");

        Assert.Equal(HttpStatusCode.OK, propio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        Assert.DoesNotContain(listadoAjeno.Items, e => e.Id == id);
    }

    [Fact]
    public async Task Un_comercio_con_varios_operadores_tiene_que_elegir_uno_valido()
    {
        using var sinElegir = await api.ClienteSinSesion().PostAsJsonAsync("api/auth/login",
            new LoginRequest { Email = Comercio2, Password = Clave });
        using var conOtro = await api.ClienteSinSesion().PostAsJsonAsync("api/auth/login",
            new LoginRequest { Email = Comercio1, Password = Clave, OperadorId = -1 });

        // El comercio 2 trabaja con los dos operadores: la API le pide elegir
        Assert.Equal(HttpStatusCode.Conflict, sinElegir.StatusCode);
        // El comercio 1 no trabaja con el operador 2
        Assert.Equal(HttpStatusCode.BadRequest, conOtro.StatusCode);
    }

    // --- 3. Reglas distintas para los dos operadores ---

    [Fact]
    public async Task Con_el_mismo_recorrido_el_operador_1_bloquea_y_el_operador_2_permite_reprogramar()
    {
        // Seeders: el operador 1 permite 2 intentos fallidos (Estándar) y el operador 2 permite 3
        var operador1 = await api.IniciarSesionAsync(Operador1);
        var operador2 = await api.IniciarSesionAsync(Operador2);
        var envioDe1 = await CrearEnvioAsync(operador1, api.IdComercio1);
        var envioDe2 = await CrearEnvioAsync(operador2, api.IdComercio2);

        string[] dosIntentosFallidos =
        [
            "EnDeposito", "AsignadoARuta", "EnTransito", "NoEntregado",
            "Reprogramado", "AsignadoARuta", "EnTransito", "NoEntregado"
        ];

        await RecorrerAsync(operador1, envioDe1, dosIntentosFallidos);
        await RecorrerAsync(operador2, envioDe2, dosIntentosFallidos);

        using var reprogramar1 = await CambiarEstadoAsync(operador1, envioDe1, "Reprogramado");
        using var reprogramar2 = await CambiarEstadoAsync(operador2, envioDe2, "Reprogramado");
        var detalle1 = await operador1.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioDe1}");

        Assert.Equal(HttpStatusCode.BadRequest, reprogramar1.StatusCode);
        Assert.Contains("tiene que pasar a devolución", await reprogramar1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, reprogramar2.StatusCode);

        Assert.Equal(2, detalle1?.IntentosFallidos);
        Assert.Equal(2, detalle1?.Regla?.MaximoIntentosEntrega);
        Assert.Equal(["EnDevolucion"], detalle1?.EstadosSiguientes);
    }

    [Fact]
    public async Task Cada_operador_ve_su_propia_configuracion()
    {
        var operador1 = await (await api.IniciarSesionAsync(Operador1))
            .GetFromJsonAsync<Contracts.Operadores.OperadorActualDto>("api/operador");
        var operador2 = await (await api.IniciarSesionAsync(Operador2))
            .GetFromJsonAsync<Contracts.Operadores.OperadorActualDto>("api/operador");

        Assert.Equal("Operador Prueba 1", operador1?.Nombre);
        Assert.Equal("Operador Prueba 2", operador2?.Nombre);
        Assert.NotEqual(operador1?.ColorPrimario, operador2?.ColorPrimario);
        Assert.Equal(2, operador1?.Reglas.Single(r => r.Modalidad == "Estandar").MaximoIntentosEntrega);
        Assert.Equal(3, operador2?.Reglas.Single(r => r.Modalidad == "Estandar").MaximoIntentosEntrega);
    }

    // --- 4. Alta desde el portal -> visualización en el Backoffice ---

    [Fact]
    public async Task Lo_que_carga_un_comercio_en_el_portal_lo_ve_el_operador_en_el_Backoffice()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var referencia = $"WEB-{Guid.NewGuid():N}"[..20];

        using var alta = await comercio.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatario,
            DireccionId = direccion,
            ReferenciaExterna = referencia,
            Bultos =
            [
                new CrearBultoRequest { PesoKg = 2, LargoCm = 30, AnchoCm = 20, AltoCm = 10, Descripcion = "Caja" },
                new CrearBultoRequest { PesoKg = 1, LargoCm = 10, AnchoCm = 10, AltoCm = 10 }
            ]
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creado = await alta.Content.ReadFromJsonAsync<EnvioResponse>();

        // El Backoffice pide el listado filtrado por origen, como lo arma EnviosApiHttp.ArmarUrlListado
        var operador = await api.IniciarSesionAsync(Operador1);
        var listado = await ListarAsync(operador, $"pagina=1&tamanioPagina=20&buscar={referencia}&canal=Portal");
        var fila = Assert.Single(listado.Items);
        var detalle = await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{fila.Id}");

        Assert.Equal(creado?.Id, fila.Id);
        Assert.Equal("Portal", fila.Canal);
        Assert.Equal("Comercio Prueba 1", fila.Comercio);
        Assert.Equal(2, fila.CantidadBultos);
        Assert.Equal("Portal", detalle?.Canal);
        Assert.Equal("Portal", Assert.Single(detalle!.Eventos).Origen);

        // El despachador del mismo operador también lo ve; el operador 2, no
        using var delDespachador = await (await api.IniciarSesionAsync(Despachador1)).GetAsync($"api/envios/{fila.Id}");
        using var delOtroOperador = await (await api.IniciarSesionAsync(Operador2)).GetAsync($"api/envios/{fila.Id}");
        Assert.Equal(HttpStatusCode.OK, delDespachador.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delOtroOperador.StatusCode);
    }

    // --- 5. Cambios de estado, rechazo de transiciones e historial ---

    [Fact]
    public async Task Un_cambio_valido_se_guarda_y_queda_en_el_historial()
    {
        var id = await CrearEnvioComoOperador1Async();
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await CambiarEstadoAsync(operador, id, "EnDeposito", "Llegó al depósito");
        var detalle = await respuesta.Content.ReadFromJsonAsync<EnvioDetalleDto>();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("EnDeposito", detalle?.Estado);
        Assert.Equal(["Admitido", "EnDeposito"], detalle?.Eventos.Select(e => e.Tipo));
        Assert.Equal("Llegó al depósito", detalle?.Eventos[1].Descripcion);
        Assert.Equal("Backoffice", detalle?.Eventos[1].Origen);
        Assert.Equal(["AsignadoARuta", "Extraviado"], detalle?.EstadosSiguientes);
    }

    [Fact]
    public async Task Una_transicion_invalida_se_rechaza_y_no_cambia_nada()
    {
        var id = await CrearEnvioComoOperador1Async();
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await CambiarEstadoAsync(operador, id, "Entregado");
        var detalle = await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("No se puede pasar el envío de Admitido a Entregado", await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("Admitido", detalle?.Estado);
        Assert.Equal(["Admitido"], detalle?.Eventos.Select(e => e.Tipo));
    }

    [Fact]
    public async Task Un_estado_que_no_existe_se_rechaza()
    {
        var id = await CrearEnvioComoOperador1Async();
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await CambiarEstadoAsync(operador, id, "Volando");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task El_historial_registra_el_recorrido_completo_hasta_la_entrega()
    {
        var id = await CrearEnvioComoOperador1Async();
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);

        await RecorrerAsync(despachador, id, "EnDeposito", "AsignadoARuta", "EnTransito");
        await RecorrerAsync(operador, id, "Entregado");

        var detalle = await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{id}");

        Assert.Equal(
            ["Admitido", "EnDeposito", "AsignadoARuta", "EnTransito", "Entregado"],
            detalle?.Eventos.Select(e => e.Tipo));
        Assert.NotNull(detalle?.FechaEntrega);
        Assert.Equal(["EnDevolucion"], detalle?.EstadosSiguientes);
    }

    // --- Ayudas ---

    private async Task<int> CrearEnvioComoOperador1Async() =>
        await CrearEnvioAsync(await api.IniciarSesionAsync(Operador1), api.IdComercio1);

    private async Task<int> CrearEnvioAsync(HttpClient cliente, int comercioId)
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(comercioId);

        using var respuesta = await cliente.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = comercioId,
            DestinatarioId = destinatario,
            DireccionId = direccion
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!.Id;
    }

    private static Task<HttpResponseMessage> CambiarEstadoAsync(
        HttpClient cliente, int id, string estado, string? descripcion = null) =>
        cliente.PatchAsJsonAsync($"api/envios/{id}/estado",
            new CambiarEstadoRequest { Estado = estado, Descripcion = descripcion });

    
private async Task RecorrerAsync(
    HttpClient cliente,
    int id,
    params string[] estados)
{
    int? rutaId = null;
    var numeroRuta = 0;

    foreach (var estado in estados)
    {
        if (estado == "AsignadoARuta")
        {
            // Obtener los recursos disponibles del operador.
            var opciones = await cliente.GetFromJsonAsync<OpcionesRutaDto>(
                "api/rutas/opciones");

            Assert.NotNull(opciones);
            Assert.NotEmpty(opciones.Repartidores);
            Assert.NotEmpty(opciones.Vehiculos);

            Assert.Contains(opciones.Envios, envio => envio.Id == id);

            numeroRuta++;

            // Cada ruta de prueba utiliza una fecha diferente
            // para no colisionar con otras rutas del fixture.
            var fecha = DateOnly.FromDateTime(DateTime.UtcNow)
                .AddDays(365 + id * 3 + numeroRuta);

            var request = new CrearRutaRequest
            {
                Nombre = $"Ruta prueba envío {id} intento {numeroRuta}",
                Fecha = fecha,
                RepartidorId = opciones.Repartidores[0].Id,
                VehiculoId = opciones.Vehiculos[0].Id,
                EnvioIds = [id]
            };

            using var respuesta = await cliente.PostAsJsonAsync(
                "api/rutas",
                request);

            var contenido = await respuesta.Content.ReadAsStringAsync();

            Assert.True(
                respuesta.IsSuccessStatusCode,
                $"No se pudo crear la ruta: " +
                $"{(int)respuesta.StatusCode} - {contenido}");

            var ruta = await respuesta.Content
                .ReadFromJsonAsync<RutaDetalleDto>();

            Assert.NotNull(ruta);

            rutaId = ruta.Id;
        }
        else if (estado == "EnTransito")
        {
            Assert.True(
                rutaId.HasValue,
                "No se puede despachar un envío sin crear su ruta.");

            using var respuesta = await cliente.PostAsync(
                $"api/rutas/{rutaId.Value}/despachar",
                null);

            var contenido = await respuesta.Content.ReadAsStringAsync();

            Assert.True(
                respuesta.IsSuccessStatusCode,
                $"No se pudo despachar la ruta: " +
                $"{(int)respuesta.StatusCode} - {contenido}");

        }
        else
        {
            // Los demás estados continúan usando
            // el endpoint habitual del Backoffice.
            using var respuesta = await CambiarEstadoAsync(
                cliente,
                id,
                estado);

            var contenido = await respuesta.Content.ReadAsStringAsync();

            Assert.True(
                respuesta.IsSuccessStatusCode,
                $"No se pudo pasar a {estado}: " +
                $"{(int)respuesta.StatusCode} - {contenido}");
        }
    }
}


    private static async Task<PaginaDto<EnvioResumenDto>> ListarAsync(HttpClient cliente, string consulta) =>
        (await cliente.GetFromJsonAsync<PaginaDto<EnvioResumenDto>>($"api/envios?{consulta}"))!;
}
