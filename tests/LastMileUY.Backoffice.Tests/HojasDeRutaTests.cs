namespace LastMileUY.Backoffice.Tests;

using System.Net;
using System.Security.Claims;
using System.Text;
using LastMileUY.Backoffice.Pages.Rutas;
using LastMileUY.Backoffice.Servicios;
using LastMileUY.Contracts.Rutas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

// Hojas de ruta en el Backoffice: el orden que arma el formulario, el cliente HTTP,
// los datos de prueba y que el despacho respete el perfil
public class HojasDeRutaTests
{
    private static readonly DateOnly Manana = DateOnly.FromDateTime(DateTime.Today).AddDays(1);

    // --- Orden de las paradas desde el formulario ---

    [Fact]
    public void Los_envios_tildados_se_ordenan_por_el_numero_que_se_les_puso()
    {
        var orden = ArmadoDeRuta.OrdenarSeleccion(
            enPantalla: [10, 20, 30, 40],
            seleccionados: [10, 30, 40],
            orden: new Dictionary<int, int?> { [10] = 3, [30] = 1, [40] = 2, [20] = 1 });

        // El 20 tiene número pero no está tildado: no va
        Assert.Equal([30, 40, 10], orden);
    }

    [Fact]
    public void Sin_numero_van_al_final_en_el_orden_de_la_pantalla()
    {
        var orden = ArmadoDeRuta.OrdenarSeleccion(
            enPantalla: [10, 20, 30, 40],
            seleccionados: [10, 20, 30, 40],
            orden: new Dictionary<int, int?> { [30] = 1, [10] = null });

        Assert.Equal([30, 10, 20, 40], orden);
    }

    [Fact]
    public void Con_el_mismo_numero_decide_la_pantalla()
    {
        var orden = ArmadoDeRuta.OrdenarSeleccion(
            enPantalla: [10, 20],
            seleccionados: [20, 10],
            orden: new Dictionary<int, int?> { [10] = 1, [20] = 1 });

        Assert.Equal([10, 20], orden);
    }

    // --- Cliente HTTP ---

    [Fact]
    public void La_url_del_listado_lleva_la_fecha_y_el_estado()
    {
        var url = RutasApiHttp.ArmarUrlListado(new FiltroRutas(new DateOnly(2026, 10, 20), "Planificada", 2));

        Assert.Equal("api/rutas?pagina=2&tamanioPagina=20&fecha=2026-10-20&estado=Planificada", url);
    }

    [Fact]
    public void Sin_filtros_la_url_solo_lleva_la_pagina()
    {
        Assert.Equal("api/rutas?pagina=1&tamanioPagina=20", RutasApiHttp.ArmarUrlListado(new FiltroRutas()));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Crear_muestra_el_motivo_que_da_la_api(HttpStatusCode codigo)
    {
        var api = CrearApi(codigo, """{ "mensaje": "El repartidor ya tiene una ruta para el 20/10/2026." }""");

        var error = await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.CrearAsync(new CrearRutaRequest()));

        Assert.Equal("El repartidor ya tiene una ruta para el 20/10/2026.", error.Message);
    }

    [Fact]
    public async Task Despachar_sin_permiso_se_rechaza()
    {
        var api = CrearApi(HttpStatusCode.Forbidden, "");

        var error = await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.DespacharAsync(1));

        Assert.Contains("permiso", error.Message);
    }

    [Fact]
    public async Task Despachar_una_ruta_que_no_existe_devuelve_null()
    {
        Assert.Null(await CrearApi(HttpStatusCode.NotFound, "").DespacharAsync(1));
    }

    [Fact]
    public async Task Obtener_lee_la_hoja_de_ruta()
    {
        const string json = """
            {
              "id": 3, "nombre": "Centro", "fecha": "2026-10-20", "estado": "Planificada",
              "repartidor": { "id": 1, "nombre": "Lucía Méndez" },
              "vehiculo": { "id": 2, "matricula": "FUR 2000", "tipo": "Furgoneta", "capacidadPesoKg": 800, "capacidadVolumenM3": 6 },
              "pesoTotalKg": 2.5, "volumenTotalM3": 0.012,
              "paradas": [{
                "orden": 1, "envioId": 201, "codigoSeguimiento": "K7M2Q9T4XW3P", "estadoEnvio": "AsignadoARuta",
                "destinatario": { "nombre": "Ana", "apellido": "Silva", "telefono": null, "email": null },
                "direccion": { "calle": "Rivera", "numero": "1234", "apartamento": null, "ciudad": "Montevideo",
                               "departamento": "Montevideo", "codigoPostal": null, "referencia": null },
                "franja": { "nombre": "Mañana", "horaInicio": "09:00:00", "horaFin": "12:00:00" },
                "cantidadBultos": 1, "pesoKg": 2.5, "volumenM3": 0.012
              }]
            }
            """;

        var ruta = await CrearApi(HttpStatusCode.OK, json).ObtenerAsync(3);

        Assert.NotNull(ruta);
        Assert.Equal(new DateOnly(2026, 10, 20), ruta.Fecha);
        var parada = Assert.Single(ruta.Paradas);
        Assert.Equal(new TimeOnly(9, 0), parada.Franja!.HoraInicio);
    }

    // --- Datos de prueba: repiten las reglas principales de la API ---

    [Fact]
    public async Task Los_datos_de_prueba_rechazan_pasarse_de_la_capacidad()
    {
        var api = new RutasApiDePrueba();

        // La moto carga 30 kg y los envíos 201 + 202 + 204 pesan 34,5
        var error = await Assert.ThrowsAsync<OperacionRechazadaException>(() =>
            api.CrearAsync(Ruta(repartidor: 1, vehiculo: 1, 201, 202, 204)));

        Assert.Contains("kg", error.Message);
    }

    [Fact]
    public async Task Los_datos_de_prueba_rechazan_franjas_fuera_de_orden_y_repartidores_repetidos()
    {
        var api = new RutasApiDePrueba();

        // 203 es de la tarde y 201 de la mañana
        await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.CrearAsync(Ruta(1, 2, 203, 201)));

        await api.CrearAsync(Ruta(1, 2, 201, 203));

        // El mismo repartidor otra vez ese día
        await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.CrearAsync(Ruta(1, 1, 205)));
    }

    [Fact]
    public async Task En_los_datos_de_prueba_un_envio_usado_deja_de_estar_disponible_y_se_despacha()
    {
        var api = new RutasApiDePrueba();

        var ruta = await api.CrearAsync(Ruta(1, 2, 201, 202));

        Assert.DoesNotContain((await api.ObtenerOpcionesAsync()).Envios, e => e.Id is 201 or 202);

        var despachada = await api.DespacharAsync(ruta.Id);
        Assert.Equal("Despachada", despachada!.Estado);
        Assert.All(despachada.Paradas, p => Assert.Equal("EnTransito", p.EstadoEnvio));

        // Dos veces no
        await Assert.ThrowsAsync<OperacionRechazadaException>(() => api.DespacharAsync(ruta.Id));
    }

    // --- Páginas ---

    [Fact]
    public async Task Despachar_con_un_perfil_sin_permiso_no_llama_a_la_api()
    {
        var api = new RutasApiDePrueba();
        var ruta = await api.CrearAsync(Ruta(1, 2, 201));
        var pagina = Detalle(api, Usuario("Repartidor"));

        await pagina.OnPostDespacharAsync(ruta.Id, CancellationToken.None);

        Assert.NotNull(pagina.ErrorDespacho);
        Assert.Equal("Planificada", (await api.ObtenerAsync(ruta.Id))!.Estado);
    }

    [Fact]
    public async Task El_despachador_despacha_desde_el_detalle()
    {
        var api = new RutasApiDePrueba();
        var ruta = await api.CrearAsync(Ruta(1, 2, 201));
        var pagina = Detalle(api, Usuario("Despachador"));

        await pagina.OnPostDespacharAsync(ruta.Id, CancellationToken.None);

        Assert.Null(pagina.ErrorDespacho);
        Assert.NotNull(pagina.Aviso);
        Assert.Equal("Despachada", (await api.ObtenerAsync(ruta.Id))!.Estado);
    }

    [Fact]
    public async Task La_nueva_ruta_manda_los_envios_en_el_orden_elegido()
    {
        var api = new RutasApiDePrueba();
        var http = new DefaultHttpContext { User = Usuario("Operador") };
        var pagina = new NuevaModel(api, NullLogger<NuevaModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataEnMemoria()),
            Nombre = "Centro",
            Fecha = Manana,
            RepartidorId = 1,
            VehiculoId = 2,
            Seleccionados = [201, 202, 205],
            Orden = new Dictionary<int, int?> { [202] = 1, [201] = 2 }
        };

        var resultado = await pagina.OnPostAsync(CancellationToken.None);

        Assert.Null(pagina.Error);

        var redireccion = Assert.IsType<RedirectToPageResult>(resultado);
        var ruta = await api.ObtenerAsync((int)redireccion.RouteValues!["id"]!);
        Assert.Equal([202, 201, 205], ruta!.Paradas.Select(p => p.EnvioId));
    }

    [Fact]
    public void Solo_operador_y_despachador_planifican_rutas()
    {
        Assert.True(Permisos.PuedePlanificarRutas(Usuario("Operador")));
        Assert.True(Permisos.PuedePlanificarRutas(Usuario("Despachador")));
        Assert.False(Permisos.PuedePlanificarRutas(Usuario("Repartidor")));
        Assert.False(Permisos.PuedePlanificarRutas(Usuario("Comercio")));
    }

    private static CrearRutaRequest Ruta(int repartidor, int vehiculo, params int[] envios) => new()
    {
        Nombre = "Ruta de prueba",
        Fecha = Manana,
        RepartidorId = repartidor,
        VehiculoId = vehiculo,
        EnvioIds = [.. envios]
    };

    private static ClaimsPrincipal Usuario(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "prueba"));

    private static DetalleModel Detalle(IRutasApi api, ClaimsPrincipal usuario)
    {
        var http = new DefaultHttpContext { User = usuario };

        return new DetalleModel(api, new SincronizacionApiDePrueba(), NullLogger<DetalleModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataEnMemoria())
        };
    }

    private static RutasApiHttp CrearApi(HttpStatusCode codigo, string cuerpo) =>
        new(new HttpClient(new RespuestaFija(codigo, cuerpo)) { BaseAddress = new Uri("http://api.test/") });

    private class RespuestaFija(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
            });
    }

    private class TempDataEnMemoria : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
