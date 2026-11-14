using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Configuracion;
using LastMileUY.Contracts.Destinatarios;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;
using DestinatarioDto = LastMileUY.Contracts.Destinatarios.DestinatarioDto;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ZonaFranjaYTarifaEnAltaDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static string Unico(string prefijo) => $"{prefijo} {Guid.NewGuid():N}"[..(prefijo.Length + 9)];

    [Fact]
    public async Task El_envio_toma_la_zona_de_la_direccion_y_el_precio_del_cuadro_vigente()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var localidad = Unico("La Paloma");

        var zona = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Costa"), null, "Rocha", [localidad]));
        var manana = await CrearFranjaAsync(operador, zona.Id, "Mañana", 9, 13);
        await CuadroVigenteConTarifaAsync(zona.Id);

        var (destinatarioId, direccionId) = await DestinatarioEnAsync(comercio, "rocha", localidad.ToUpperInvariant());

        var opciones = (await comercio.GetFromJsonAsync<OpcionesEntregaDto>($"api/envios/opciones-entrega?direccionId={direccionId}"))!;
        Assert.Equal(zona.Nombre, opciones.Zona);
        Assert.Equal(manana.Id, Assert.Single(opciones.Franjas).Id);

        var estandar = await CrearEnvioAsync(comercio, destinatarioId, direccionId, pesoKg: 2.5m, franjaId: manana.Id);
        Assert.Equal(zona.Nombre, estandar.Zona);
        Assert.Equal("Mañana", estandar.Franja);
        Assert.Equal(275.00m, estandar.CostoEnvio);

        var detalle = (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{estandar.Id}"))!;
        Assert.Equal(zona.Nombre, detalle.Zona);
        Assert.Equal(new TimeOnly(9, 0), detalle.Franja!.HoraInicio);
        Assert.Equal(275.00m, detalle.CostoEnvio);

        var urgente = await CrearEnvioAsync(comercio, destinatarioId, direccionId, pesoKg: 2.5m, modalidad: "Urgente");
        var pesado = await CrearEnvioAsync(comercio, destinatarioId, direccionId, pesoKg: 25m);

        Assert.Equal("Urgente", urgente.Modalidad);
        Assert.Equal(zona.Nombre, urgente.Zona);
        Assert.Null(urgente.CostoEnvio);
        Assert.Null(pesado.CostoEnvio);
    }

    [Fact]
    public async Task Una_direccion_fuera_de_cobertura_se_crea_sin_zona_ni_precio_y_no_admite_franja()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);

        var zona = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Litoral"), null, "Salto", [Unico("Constitución")]));
        var franja = await CrearFranjaAsync(operador, zona.Id, "Tarde", 14, 18);

        var (destinatarioId, direccionId) = await DestinatarioEnAsync(comercio, "Salto", Unico("Belén"));

        var opciones = (await comercio.GetFromJsonAsync<OpcionesEntregaDto>($"api/envios/opciones-entrega?direccionId={direccionId}"))!;
        Assert.Null(opciones.Zona);
        Assert.Empty(opciones.Franjas);

        var sinFranja = await CrearEnvioAsync(comercio, destinatarioId, direccionId, pesoKg: 1m);
        Assert.Null(sinFranja.Zona);
        Assert.Null(sinFranja.CostoEnvio);

        using var conFranja = await comercio.PostAsJsonAsync("api/envios", Pedido(destinatarioId, direccionId, 1m, franjaId: franja.Id));
        Assert.Equal(HttpStatusCode.BadRequest, conFranja.StatusCode);
    }

    [Fact]
    public async Task Una_franja_de_otra_zona_o_de_otro_operador_es_un_400()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var otroOperador = await api.IniciarSesionAsync(Operador2);
        var comercio = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var localidad = Unico("Trinidad");

        var zona = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Centro"), null, "Flores", [localidad]));
        await CrearFranjaAsync(operador, zona.Id, "Mañana", 9, 13);
        var otraZona = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Afuera"), null, "Flores", [Unico("Ismael Cortinas")]));
        var franjaDeOtraZona = await CrearFranjaAsync(operador, otraZona.Id, "Mañana", 9, 13);
        var zonaAjena = await CrearZonaAsync(otroOperador, new GuardarZonaRequest(Unico("Ajena"), null));
        var franjaAjena = await CrearFranjaAsync(otroOperador, zonaAjena.Id, "Mañana", 9, 13);

        var (destinatarioId, direccionId) = await DestinatarioEnAsync(comercio, "Flores", localidad);

        using var deOtraZona = await comercio.PostAsJsonAsync("api/envios", Pedido(destinatarioId, direccionId, 1m, franjaId: franjaDeOtraZona.Id));
        using var deOtroOperador = await comercio.PostAsJsonAsync("api/envios", Pedido(destinatarioId, direccionId, 1m, franjaId: franjaAjena.Id));
        using var modalidadInvalida = await comercio.PostAsJsonAsync("api/envios", Pedido(destinatarioId, direccionId, 1m, modalidad: "Express"));

        Assert.Equal(HttpStatusCode.BadRequest, deOtraZona.StatusCode);
        Assert.Contains(zona.Nombre, await deOtraZona.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, deOtroOperador.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, modalidadInvalida.StatusCode);
    }

    [Fact]
    public async Task Dos_zonas_activas_no_pueden_cubrir_la_misma_localidad()
    {
        var operador = await api.IniciarSesionAsync(Operador2);
        var localidad = Unico("Durazno");

        var primera = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Ciudad"), null, "Durazno", [localidad, "Santa Bernardina"]));

        using var repetida = await operador.PostAsJsonAsync("api/operador/zonas",
            new GuardarZonaRequest(Unico("Otra"), null, "durazno", [localidad.ToUpperInvariant()]));
        using var sinDepartamento = await operador.PostAsJsonAsync("api/operador/zonas",
            new GuardarZonaRequest(Unico("Suelta"), null, null, ["Sarandí del Yí"]));
        using var inventado = await operador.PostAsJsonAsync("api/operador/zonas",
            new GuardarZonaRequest(Unico("Mal"), null, "Narnia"));

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinDepartamento.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inventado.StatusCode);

        using var baja = await operador.PutAsJsonAsync($"api/operador/zonas/{primera.Id}/activa", new CambiarActivoRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);

        var reemplazo = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Nueva"), null, "Durazno", [localidad]));
        Assert.Equal("Durazno", reemplazo.Departamento);

        using var reactivar = await operador.PutAsJsonAsync($"api/operador/zonas/{primera.Id}/activa", new CambiarActivoRequest(true));
        Assert.Equal(HttpStatusCode.Conflict, reactivar.StatusCode);
    }

    [Fact]
    public async Task La_api_publica_acepta_modalidad_y_franja_y_devuelve_el_costo()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var localidad = Unico("Chuy");

        var zona = await CrearZonaAsync(operador, new GuardarZonaRequest(Unico("Frontera"), null, "Rocha", [localidad]));
        var franja = await CrearFranjaAsync(operador, zona.Id, "Tarde", 14, 18);
        await CuadroVigenteConTarifaAsync(zona.Id);

        var (destinatarioId, direccionId) = await DestinatarioEnAsync(portal, "Rocha", localidad);

        using var claveCreada = await portal.PostAsJsonAsync("api/claves-api", new CrearClaveApiRequest { Nombre = Unico("Tienda") });
        var clave = (await claveCreada.Content.ReadFromJsonAsync<ClaveApiCreadaDto>())!.Clave;
        var sistema = api.ClienteSinSesion();
        sistema.DefaultRequestHeaders.Add("X-Api-Key", clave);

        using var respuesta = await sistema.PostAsJsonAsync("api/v1/envios", new EnvioApiRequest
        {
            DestinatarioId = destinatarioId,
            DireccionId = direccionId,
            Modalidad = "Estandar",
            FranjaHorariaId = franja.Id,
            Bultos = [new CrearBultoRequest { PesoKg = 4m, LargoCm = 30, AnchoCm = 20, AltoCm = 10 }]
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var envio = (await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!;
        Assert.Equal("Tarde", envio.Franja);
        Assert.Equal(320.00m, envio.CostoEnvio);
    }

    private static async Task<ZonaDto> CrearZonaAsync(HttpClient operador, GuardarZonaRequest request)
    {
        using var respuesta = await operador.PostAsJsonAsync("api/operador/zonas", request);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ZonaDto>())!;
    }

    private static async Task<FranjaHorariaDto> CrearFranjaAsync(HttpClient operador, int zonaId, string nombre, int desde, int hasta)
    {
        using var respuesta = await operador.PostAsJsonAsync($"api/operador/zonas/{zonaId}/franjas",
            new GuardarFranjaHorariaRequest(nombre, new TimeOnly(desde, 0), new TimeOnly(hasta, 0)));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<FranjaHorariaDto>())!;
    }

    private async Task CuadroVigenteConTarifaAsync(int zonaId) =>
        await api.EjecutarAsync($"""
            INSERT INTO "CuadrosTarifarios" ("OperadorId", "Version", "VigenciaDesde", "FechaCreacion")
            VALUES ({api.IdOperador1}, {Random.Shared.Next(5_000, 900_000)}, now() - interval '1 day', now())
            ON CONFLICT DO NOTHING;
            INSERT INTO "Tarifas" ("CuadroTarifarioId", "Nombre", "ZonaId", "Modalidad", "PesoMinimoKg", "PesoMaximoKg",
                                   "VolumenMinimoM3", "VolumenMaximoM3", "PrecioBase", "PrecioPorKgAdicional", "Activa")
            SELECT c."Id", 'Hasta 10 kg', {zonaId}, 1, 0, 10, 0, 1, 200, 30, true
            FROM "CuadrosTarifarios" c
            WHERE c."OperadorId" = {api.IdOperador1}
              AND c."VigenciaDesde" <= now() AND (c."VigenciaHasta" IS NULL OR c."VigenciaHasta" > now())
            ORDER BY c."Version" DESC
            LIMIT 1;
            """);

    private async Task<(int DestinatarioId, int DireccionId)> DestinatarioEnAsync(HttpClient comercio, string departamento, string ciudad)
    {
        using var alta = await comercio.PostAsJsonAsync("api/destinatarios", new CrearDestinatarioRequest
        {
            ComercioId = api.IdComercio1,
            Nombre = "Ana",
            Apellido = "Suárez",
            Direccion = new CrearDireccionRequest
            {
                Calle = "Av. Principal",
                Numero = "100",
                Ciudad = ciudad,
                Departamento = departamento
            }
        });

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var destinatario = (await alta.Content.ReadFromJsonAsync<DestinatarioDto>())!;
        return (destinatario.Id, destinatario.Direcciones.Single().Id);
    }

    private CrearEnvioRequest Pedido(int destinatarioId, int direccionId, decimal pesoKg, string? modalidad = null, int? franjaId = null) => new()
    {
        ComercioId = api.IdComercio1,
        DestinatarioId = destinatarioId,
        DireccionId = direccionId,
        Modalidad = modalidad,
        FranjaHorariaId = franjaId,
        Bultos = [new CrearBultoRequest { PesoKg = pesoKg, LargoCm = 30, AnchoCm = 20, AltoCm = 10 }]
    };

    private async Task<EnvioResponse> CrearEnvioAsync(
        HttpClient comercio, int destinatarioId, int direccionId, decimal pesoKg, string? modalidad = null, int? franjaId = null)
    {
        using var respuesta = await comercio.PostAsJsonAsync("api/envios", Pedido(destinatarioId, direccionId, pesoKg, modalidad, franjaId));
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EnvioResponse>())!;
    }
}
