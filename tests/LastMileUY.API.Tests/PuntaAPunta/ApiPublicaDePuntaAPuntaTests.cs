namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using System.Text.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;
using static ApiDePuntaAPunta;

// API pública v1 (docs/api-publica.md), por HTTP contra la API real: la clave se crea en el portal
// (con la sesión del comercio) y después el "sistema del comercio" usa solo la clave.
// Cubre: autenticación y su alcance, revocación, vínculo inactivo, aislamiento entre comercios,
// canal Api, importación masiva idempotente (req. 7), topes por clave y la documentación navegable.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ApiPublicaDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    [Fact]
    public async Task La_clave_creada_en_el_portal_sirve_para_la_v1_y_el_secreto_se_ve_una_sola_vez()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var creada = await CrearClaveAsync(portal, "Tienda online");

        Assert.StartsWith("lmuy_", creada.Clave);
        Assert.StartsWith(creada.Datos.Prefijo, creada.Clave);
        Assert.True(creada.Datos.Activa);

        // El listado no trae el secreto: solo el prefijo para reconocerla
        using (var listado = await portal.GetAsync("api/claves-api"))
        {
            var texto = await listado.Content.ReadAsStringAsync();
            Assert.Contains(creada.Datos.Prefijo, texto);
            Assert.DoesNotContain(creada.Clave, texto);
        }

        // Ni en la base: solo su hash
        Assert.Equal(0, await api.ConsultarCantidadAsync(
            $"SELECT count(*) FROM \"ClavesApi\" WHERE \"Hash\" = '{creada.Clave}' OR \"Prefijo\" = '{creada.Clave}'"));

        var sistema = ClienteConClave(creada.Clave);
        using var envios = await sistema.GetAsync("api/v1/envios");

        Assert.Equal(HttpStatusCode.OK, envios.StatusCode);
        var pagina = (await envios.Content.ReadFromJsonAsync<PaginaDto<EnvioResumenDto>>())!;
        Assert.NotEmpty(pagina.Items);

        // Queda registrado cuándo se usó por última vez
        var claves = (await portal.GetFromJsonAsync<List<ClaveApiDto>>("api/claves-api"))!;
        Assert.NotNull(claves.Single(c => c.Id == creada.Datos.Id).UltimoUso);
    }

    [Fact]
    public async Task Sin_clave_valida_es_401_y_la_clave_no_abre_el_resto_de_la_api()
    {
        using (var sinClave = await api.ClienteSinSesion().GetAsync("api/v1/envios"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, sinClave.StatusCode);
            Assert.Contains("X-Api-Key", sinClave.Headers.WwwAuthenticate.ToString());
        }

        using (var inventada = await ClienteConClave("lmuy_estoNoEsUnaClaveDeVerdad").GetAsync("api/v1/envios"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, inventada.StatusCode);
        }

        // La sesión del portal no sirve en la v1: es solo para sistemas, con clave
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        using (var conCookie = await portal.GetAsync("api/v1/envios"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, conCookie.StatusCode);
        }

        // Y la clave no sirve fuera de la v1: ni para el resto de la API ni para crear más claves
        var sistema = ClienteConClave((await CrearClaveAsync(portal, "ERP")).Clave);

        using (var interna = await sistema.GetAsync("api/envios"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, interna.StatusCode);
        }

        using (var otraClave = await sistema.PostAsJsonAsync("api/claves-api", new CrearClaveApiRequest { Nombre = "Otra" }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, otraClave.StatusCode);
        }
    }

    [Fact]
    public async Task Muchas_claves_invalidas_desde_una_IP_se_frenan_con_429_sin_afectar_a_otras_IP()
    {
        var buena = (await CrearClaveAsync(await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Misma IP")).Clave;

        // Cada prueba con su IP inventada (documentación, RFC 5737): no frena a las demás pruebas
        var atacante = IPAddress.Parse("203.0.113." + Random.Shared.Next(1, 250));

        for (var i = 0; i < 30; i++)
            Assert.Equal(StatusCodes.Status401Unauthorized, (await PedirAsync(atacante, "lmuy_live_inventada" + i)).Response.StatusCode);

        var frenado = await PedirAsync(atacante, "lmuy_live_otra");
        Assert.Equal(StatusCodes.Status429TooManyRequests, frenado.Response.StatusCode);
        Assert.True(int.Parse(frenado.Response.Headers.RetryAfter!) > 0);

        // Mientras dura, desde esa IP se frena cualquier clave (no se consulta la base)
        Assert.Equal(StatusCodes.Status429TooManyRequests, (await PedirAsync(atacante, buena)).Response.StatusCode);

        // Desde otra IP la clave buena anda
        Assert.Equal(StatusCodes.Status200OK,
            (await PedirAsync(IPAddress.Parse("198.51.100." + Random.Shared.Next(1, 250)), buena)).Response.StatusCode);
    }

    private Task<Microsoft.AspNetCore.Http.HttpContext> PedirAsync(IPAddress ip, string clave) =>
        api.Server.SendAsync(contexto =>
        {
            contexto.Request.Method = "GET";
            contexto.Request.Scheme = "https";
            contexto.Request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");
            contexto.Request.Path = "/api/v1/destinatarios";
            contexto.Request.Headers["X-Api-Key"] = clave;
            contexto.Connection.RemoteIpAddress = ip;
        });

    [Fact]
    public async Task Revocar_la_clave_corta_el_acceso_en_el_pedido_siguiente()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var creada = await CrearClaveAsync(portal, "Para revocar");
        var sistema = ClienteConClave(creada.Clave);

        using (var antes = await sistema.GetAsync("api/v1/destinatarios"))
        {
            Assert.Equal(HttpStatusCode.OK, antes.StatusCode);
        }

        using (var revocar = await portal.PostAsync($"api/claves-api/{creada.Datos.Id}/revocar", null))
        {
            Assert.Equal(HttpStatusCode.OK, revocar.StatusCode);
            Assert.False((await revocar.Content.ReadFromJsonAsync<ClaveApiDto>())!.Activa);
        }

        using var despues = await sistema.GetAsync("api/v1/destinatarios");
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
    }

    [Fact]
    public async Task Si_el_comercio_deja_de_trabajar_con_el_operador_la_clave_responde_403()
    {
        // Comercio 2 con el operador 2: el vínculo que agrega el fixture, para no tocar los de los seeders
        var portal = await api.IniciarSesionAsync(Comercio2, api.IdOperador2);
        var sistema = ClienteConClave((await CrearClaveAsync(portal, "Vínculo")).Clave);

        var vinculo = $"\"OperadorId\" = {api.IdOperador2} AND \"ComercioId\" = {api.IdComercio2}";

        try
        {
            await api.EjecutarAsync($"UPDATE \"OperadoresComercios\" SET \"Activo\" = false WHERE {vinculo}");

            using var respuesta = await sistema.GetAsync("api/v1/envios");
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
        finally
        {
            await api.EjecutarAsync($"UPDATE \"OperadoresComercios\" SET \"Activo\" = true WHERE {vinculo}");
        }
    }

    [Fact]
    public async Task La_clave_de_un_comercio_no_ve_los_envios_de_otro_y_los_que_crea_entran_por_el_canal_Api()
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var sistema1 = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Comercio 1")).Clave);

        using var alta = await sistema1.PostAsJsonAsync("api/v1/envios", Envio(destinatario, direccion, "API-" + Guid.NewGuid().ToString("N")[..10]));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var envio = (await alta.Content.ReadFromJsonAsync<EnvioResponse>())!;
        Assert.Equal(api.IdComercio1, envio.ComercioId);

        var detalle = (await sistema1.GetFromJsonAsync<EnvioDetalleDto>($"api/v1/envios/{envio.Id}"))!;
        Assert.Equal("Api", detalle.Canal);

        // El comercio 2 trabaja con el mismo operador, pero el envío no es suyo: para él no existe
        var sistema2 = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio2, api.IdOperador1), "Comercio 2")).Clave);

        using var ajeno = await sistema2.GetAsync($"api/v1/envios/{envio.Id}");
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);

        // Ni puede usar destinatarios del comercio 1
        using var conDestinatarioAjeno = await sistema2.PostAsJsonAsync("api/v1/envios", Envio(destinatario, direccion, null));
        Assert.Equal(HttpStatusCode.BadRequest, conDestinatarioAjeno.StatusCode);
    }

    [Fact]
    public async Task Reimportar_el_lote_completo_o_una_parte_no_duplica_envios()
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var sistema = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Importador")).Clave);

        var lote = "LOTE-" + Guid.NewGuid().ToString("N")[..8];
        var pedido = new ImportarEnviosRequest
        {
            Envios =
            [
                Envio(destinatario, direccion, $"{lote}-1"),
                Envio(destinatario, direccion, $"{lote}-2"),
                Envio(destinatario, direccion, null), // sin referencia: no se puede importar sin duplicar
                Envio(destinatario, direccion, $"{lote}-1"), // repetida dentro del mismo lote
                new EnvioApiRequest { ReferenciaExterna = $"{lote}-3", DestinatarioId = destinatario, DireccionId = direccion } // sin bultos
            ]
        };

        var primera = await ImportarAsync(sistema, pedido);

        Assert.Equal((2, 1, 2), (primera.Creados, primera.Existentes, primera.Rechazados));
        Assert.Equal(
            [ResultadosImportacion.Creado, ResultadosImportacion.Creado, ResultadosImportacion.Rechazado,
             ResultadosImportacion.Existente, ResultadosImportacion.Rechazado],
            primera.Resultados.Select(r => r.Resultado));
        Assert.Equal(primera.Resultados[0].EnvioId, primera.Resultados[3].EnvioId);
        Assert.Contains("bulto", primera.Resultados[4].Mensaje);

        // El mismo lote otra vez: nada nuevo, y cada referencia devuelve el envío que ya estaba
        var segunda = await ImportarAsync(sistema, pedido);

        Assert.Equal((0, 3, 2), (segunda.Creados, segunda.Existentes, segunda.Rechazados));
        Assert.Equal(primera.Resultados[1].EnvioId, segunda.Resultados[1].EnvioId);

        // Una parte, con la fila corregida: solo se crea la que faltaba
        var tercera = await ImportarAsync(sistema, new ImportarEnviosRequest
        {
            Envios = [Envio(destinatario, direccion, $"{lote}-2"), Envio(destinatario, direccion, $"{lote}-3")]
        });

        Assert.Equal((1, 1, 0), (tercera.Creados, tercera.Existentes, tercera.Rechazados));

        Assert.Equal(3, await api.ConsultarCantidadAsync(
            $"SELECT count(*) FROM \"Envios\" WHERE \"ReferenciaExterna\" LIKE '{lote}-%'"));
        var importado = (await sistema.GetFromJsonAsync<EnvioDetalleDto>($"api/v1/envios/{primera.Resultados[0].EnvioId}"))!;
        Assert.Equal("Api", importado.Canal);
    }

    [Fact]
    public async Task Un_lote_vacio_o_de_mas_de_100_envios_se_rechaza_entero()
    {
        var sistema = ClienteConClave((await CrearClaveAsync(
            await api.IniciarSesionAsync(Comercio1, api.IdOperador1), "Topes")).Clave);

        using (var vacio = await sistema.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, vacio.StatusCode);
        }

        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        using var grande = await sistema.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest
        {
            Envios = Enumerable.Range(0, 101).Select(i => Envio(destinatario, direccion, $"TOPE-{i}")).ToList()
        });

        Assert.Equal(HttpStatusCode.BadRequest, grande.StatusCode);
        Assert.Equal(0, await api.ConsultarCantidadAsync("SELECT count(*) FROM \"Envios\" WHERE \"ReferenciaExterna\" LIKE 'TOPE-%'"));
    }

    [Fact]
    public async Task El_tope_de_lotes_es_por_clave_y_al_pasarlo_responde_429_con_Retry_After()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var apurado = ClienteConClave((await CrearClaveAsync(portal, "Apurado")).Clave);
        var tranquilo = ClienteConClave((await CrearClaveAsync(portal, "Tranquilo")).Clave);

        // 10 lotes por minuto por clave (los vacíos se rechazan, pero cuentan igual)
        for (var i = 0; i < 10; i++)
        {
            using var permitido = await apurado.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest());
            Assert.Equal(HttpStatusCode.BadRequest, permitido.StatusCode);
        }

        using (var frenado = await apurado.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest()))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, frenado.StatusCode);
            Assert.True(frenado.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        }

        // Diferenciado: las consultas tienen su propio tope, y las demás claves no se enteran
        using (var consulta = await apurado.GetAsync("api/v1/envios"))
        {
            Assert.Equal(HttpStatusCode.OK, consulta.StatusCode);
        }

        using var otra = await tranquilo.PostAsJsonAsync("api/v1/envios/lote", new ImportarEnviosRequest());
        Assert.Equal(HttpStatusCode.BadRequest, otra.StatusCode);
    }

    [Fact]
    public async Task La_documentacion_navegable_es_publica_y_solo_muestra_la_v1()
    {
        var cliente = api.ClienteSinSesion();

        using var openApi = await cliente.GetAsync("api/v1/openapi.json");
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);

        using var documento = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        var raiz = documento.RootElement;
        var rutas = raiz.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("/api/v1/envios/lote", rutas);
        Assert.Contains("/api/v1/destinatarios", rutas);
        Assert.All(rutas, ruta => Assert.StartsWith("/api/v1/", ruta));

        var esquema = raiz.GetProperty("components").GetProperty("securitySchemes").GetProperty("ClaveApi");
        Assert.Equal("apiKey", esquema.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", esquema.GetProperty("name").GetString());

        using var pagina = await cliente.GetAsync("api/v1/docs/");
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("text/html", pagina.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Dos_importaciones_del_mismo_lote_a_la_vez_no_duplican_envios()
    {
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var sistemaA = ClienteConClave((await CrearClaveAsync(portal, "Importador A")).Clave);
        var sistemaB = ClienteConClave((await CrearClaveAsync(portal, "Importador B")).Clave);

        var lote = "PAR-" + Guid.NewGuid().ToString("N")[..8];
        var pedido = new ImportarEnviosRequest
        {
            Envios = Enumerable.Range(1, 20).Select(i => Envio(destinatario, direccion, $"{lote}-{i}")).ToList()
        };

        // Las dos a la vez: chocan en las mismas referencias, y el índice único frena a la que llega segunda
        var resultados = await Task.WhenAll(ImportarAsync(sistemaA, pedido), ImportarAsync(sistemaB, pedido));

        Assert.All(resultados.SelectMany(r => r.Resultados), r =>
            Assert.Contains(r.Resultado, new[] { ResultadosImportacion.Creado, ResultadosImportacion.Existente }));
        Assert.Equal(20, resultados.Sum(r => r.Creados));
        Assert.Equal(20, await api.ConsultarCantidadAsync(
            $"SELECT count(*) FROM \"Envios\" WHERE \"ReferenciaExterna\" LIKE '{lote}-%'"));

        // Para cada referencia, las dos respuestas apuntan al mismo envío
        for (var i = 0; i < 20; i++)
            Assert.Equal(resultados[0].Resultados[i].EnvioId, resultados[1].Resultados[i].EnvioId);
    }

    // Lo que garantiza "versionada": la forma de la v1 (rutas, parámetros, campos de pedidos y
    // respuestas) está guardada en ContratoApiPublicaV1.json. Si un cambio la altera, esta prueba falla.
    // Si el cambio es compatible (agrega un campo o un endpoint), se actualiza el archivo a propósito
    // corriendo la prueba con ACTUALIZAR_CONTRATO=1. Si no es compatible, va en una /api/v2.
    [Fact]
    public async Task El_contrato_de_la_v1_no_cambia_sin_querer()
    {
        var archivo = Path.Combine(CarpetaDeEsteArchivo(), "ContratoApiPublicaV1.json");

        using var respuesta = await api.ClienteSinSesion().GetAsync("api/v1/openapi.json");
        var actual = System.Text.Json.Nodes.JsonNode.Parse(await respuesta.Content.ReadAsStringAsync())!.AsObject();

        // La dirección del servidor depende de dónde corre la API: no es parte del contrato
        actual.Remove("servers");

        if (Environment.GetEnvironmentVariable("ACTUALIZAR_CONTRATO") == "1")
        {
            await File.WriteAllTextAsync(archivo,
                actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        }

        Assert.True(File.Exists(archivo), $"Falta {archivo}: generalo con ACTUALIZAR_CONTRATO=1.");
        var guardado = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(archivo));

        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(guardado, actual),
            "El contrato de /api/v1 cambió. Si es compatible, actualizá ContratoApiPublicaV1.json con ACTUALIZAR_CONTRATO=1; si no, va en /api/v2.");
    }

    private static string CarpetaDeEsteArchivo([System.Runtime.CompilerServices.CallerFilePath] string ruta = "") =>
        Path.GetDirectoryName(ruta)!;

    private static async Task<ClaveApiCreadaDto> CrearClaveAsync(HttpClient portal, string nombre)
    {
        using var respuesta = await portal.PostAsJsonAsync("api/claves-api", new CrearClaveApiRequest { Nombre = nombre });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ClaveApiCreadaDto>())!;
    }

    // El sistema del comercio: sin cookies, solo la clave
    private HttpClient ClienteConClave(string clave)
    {
        var cliente = api.ClienteSinSesion();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", clave);
        return cliente;
    }

    private static async Task<ImportarEnviosResponse> ImportarAsync(HttpClient sistema, ImportarEnviosRequest pedido)
    {
        using var respuesta = await sistema.PostAsJsonAsync("api/v1/envios/lote", pedido);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ImportarEnviosResponse>())!;
    }

    private static EnvioApiRequest Envio(int destinatario, int direccion, string? referencia) => new()
    {
        ReferenciaExterna = referencia,
        DestinatarioId = destinatario,
        DireccionId = direccion,
        Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
    };
}
