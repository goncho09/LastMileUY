namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Destinatarios;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using static ApiDePuntaAPunta;
using DestinatarioDto = LastMileUY.Contracts.Destinatarios.DestinatarioDto;
using DireccionDto = LastMileUY.Contracts.Destinatarios.DireccionDto;

// Alta de destinatarios y de sus direcciones por HTTP con los usuarios reales: el comercio carga
// los suyos y el operador los de sus comercios; nadie carga en un comercio ajeno; los datos se
// validan; y lo cargado sirve enseguida para crear un envío (con su ubicación para ordenar la ruta).
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class DestinatariosDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    [Fact]
    public async Task El_comercio_da_de_alta_un_destinatario_y_le_crea_un_envio_con_ubicacion()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);

        // Sin ComercioId: el usuario de un comercio carga en el suyo. El departamento sin tilde se acepta
        var pedido = Destinatario(departamento: "san jose", latitud: -34.3375m, longitud: -56.7136m);
        pedido.Email = "  Lucia.Perez@Correo.UY ";

        using var alta = await comercio.PostAsJsonAsync("api/destinatarios", pedido);

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creado = (await alta.Content.ReadFromJsonAsync<DestinatarioDto>())!;
        Assert.Equal(api.IdComercio1, creado.ComercioId);
        Assert.Equal("lucia.perez@correo.uy", creado.Email);
        var direccion = Assert.Single(creado.Direcciones);
        Assert.Equal("San José", direccion.Departamento);
        Assert.Equal(-34.3375m, direccion.Latitud);

        // Aparece en el listado que usa el formulario de envíos
        var listado = (await comercio.GetFromJsonAsync<List<DestinatarioDto>>("api/destinatarios"))!;
        Assert.Contains(listado, d => d.Id == creado.Id && d.Direcciones.Single().Longitud == -56.7136m);

        // Y se le puede mandar un envío, que copia la ubicación de la dirección
        using var envio = await comercio.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = creado.Id,
            DireccionId = direccion.Id,
            Bultos = [new CrearBultoRequest { PesoKg = 1, LargoCm = 20, AnchoCm = 15, AltoCm = 10 }]
        });
        Assert.Equal(HttpStatusCode.Created, envio.StatusCode);
        var envioCreado = (await envio.Content.ReadFromJsonAsync<EnvioResponse>())!;

        var operador = await api.IniciarSesionAsync(Operador1);
        using (var deposito = await operador.PatchAsJsonAsync($"api/envios/{envioCreado.Id}/estado",
                   new CambiarEstadoRequest { Estado = "EnDeposito" }))
        {
            Assert.True(deposito.IsSuccessStatusCode);
        }

        var opciones = (await operador.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        Assert.True(opciones.Envios.Single(e => e.Id == envioCreado.Id).TieneUbicacion);
    }

    [Fact]
    public async Task El_operador_carga_destinatarios_solo_en_los_comercios_con_los_que_trabaja()
    {
        var operador1 = await api.IniciarSesionAsync(Operador1);
        var operador2 = await api.IniciarSesionAsync(Operador2);
        await ComercioSoloDelOperador1Async();

        // El operador tiene que decir de qué comercio es
        using (var sinComercio = await operador1.PostAsJsonAsync("api/destinatarios", Destinatario()))
        {
            Assert.Equal(HttpStatusCode.BadRequest, sinComercio.StatusCode);
        }

        // El operador 2 no trabaja con ese comercio
        var ajeno = Destinatario();
        ajeno.ComercioId = ComercioSoloDelOperador1;
        using (var deOtro = await operador2.PostAsJsonAsync("api/destinatarios", ajeno))
        {
            Assert.Equal(HttpStatusCode.BadRequest, deOtro.StatusCode);
        }

        // El operador 1 sí (y sin ubicación también se puede)
        using var alta = await operador1.PostAsJsonAsync("api/destinatarios", ajeno);

        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creado = (await alta.Content.ReadFromJsonAsync<DestinatarioDto>())!;
        Assert.Equal(ComercioSoloDelOperador1, creado.ComercioId);
        Assert.Null(creado.Direcciones.Single().Latitud);
    }

    // Los seeders vinculan los dos comercios de prueba con los dos operadores: para tener uno con el
    // que el operador 2 no trabaja, se arma aparte (con un destinatario), con el superusuario
    private const int ComercioSoloDelOperador1 = 9001;
    private const int DestinatarioDelComercioSoloDelOperador1 = 9001;

    private Task ComercioSoloDelOperador1Async() => api.EjecutarAsync($"""
        INSERT INTO "Comercios"("Id", "Nombre", "Activo", "FechaCreacion")
            VALUES ({ComercioSoloDelOperador1}, 'Comercio solo del operador 1', true, now())
            ON CONFLICT DO NOTHING;
        INSERT INTO "OperadoresComercios"("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
            VALUES ({api.IdOperador1}, {ComercioSoloDelOperador1}, now(), true)
            ON CONFLICT DO NOTHING;
        INSERT INTO "Destinatarios"("Id", "ComercioId", "Nombre", "Apellido", "FechaCreacion")
            VALUES ({DestinatarioDelComercioSoloDelOperador1}, {ComercioSoloDelOperador1}, 'Ana', 'Suárez', now())
            ON CONFLICT DO NOTHING;
        """);

    [Fact]
    public async Task Un_comercio_no_carga_destinatarios_en_otro_comercio_y_los_demas_perfiles_no_cargan()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);
        var otro = Destinatario();
        otro.ComercioId = api.IdComercio2;

        using (var enOtro = await comercio.PostAsJsonAsync("api/destinatarios", otro))
        {
            Assert.Equal(HttpStatusCode.Forbidden, enOtro.StatusCode);
        }

        foreach (var email in new[] { Despachador1, Repartidor1 })
        {
            var cliente = await api.IniciarSesionAsync(email);
            var pedido = Destinatario();
            pedido.ComercioId = api.IdComercio1;

            using var respuesta = await cliente.PostAsJsonAsync("api/destinatarios", pedido);
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
    }

    public static TheoryData<string, Action<CrearDestinatarioRequest>> DatosInvalidos => new()
    {
        { "Completá el nombre", p => p.Nombre = "   " },
        { "Completá el apellido", p => p.Apellido = "" },
        { "email no es válido", p => p.Email = "sin-arroba" },
        { "admite como máximo 30", p => p.Telefono = new string('9', 31) },
        { "Completá la calle", p => p.Direccion.Calle = "" },
        { "admite como máximo 150", p => p.Direccion.Calle = new string('C', 151) },
        { "19 de Uruguay", p => p.Direccion.Departamento = "Buenos Aires" },
        { "o ninguna de las dos", p => p.Direccion.Latitud = -34.9m },
        { "entre -90 y 90", p => (p.Direccion.Latitud, p.Direccion.Longitud) = (-120m, -56m) }
    };

    [Theory]
    [MemberData(nameof(DatosInvalidos))]
    public async Task Los_datos_invalidos_son_un_400_con_el_motivo(string motivo, Action<CrearDestinatarioRequest> romper)
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);
        var pedido = Destinatario();
        romper(pedido);

        using var respuesta = await comercio.PostAsJsonAsync("api/destinatarios", pedido);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains(motivo, cuerpo.GetProperty("mensaje").GetString());
    }

    [Fact]
    public async Task Se_agrega_otra_direccion_a_un_destinatario_propio_pero_no_a_uno_ajeno()
    {
        var comercio1 = await api.IniciarSesionAsync(Comercio1);
        var operador2 = await api.IniciarSesionAsync(Operador2);
        var (destinatario, _) = await api.DestinatarioDeAsync(api.IdComercio1);
        await ComercioSoloDelOperador1Async();

        using (var alta = await comercio1.PostAsJsonAsync($"api/destinatarios/{destinatario}/direcciones",
                   Direccion("Montevideo", -34.9011m, -56.1645m)))
        {
            Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
            var nueva = (await alta.Content.ReadFromJsonAsync<DireccionDto>())!;

            var listado = (await comercio1.GetFromJsonAsync<List<DestinatarioDto>>("api/destinatarios"))!;
            Assert.Contains(listado.Single(d => d.Id == destinatario).Direcciones, d => d.Id == nueva.Id);
        }

        // Para el operador 2 el destinatario de un comercio con el que no trabaja no existe
        using (var ajena = await operador2.PostAsJsonAsync(
                   $"api/destinatarios/{DestinatarioDelComercioSoloDelOperador1}/direcciones", Direccion("Montevideo")))
        {
            Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        }

        // Una dirección inválida no se guarda
        using var invalida = await comercio1.PostAsJsonAsync($"api/destinatarios/{destinatario}/direcciones",
            Direccion("Atlántida"));
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }

    private static CrearDestinatarioRequest Destinatario(
        string departamento = "Montevideo", decimal? latitud = null, decimal? longitud = null) => new()
        {
            Nombre = "Lucía",
            Apellido = "Pérez",
            Telefono = "099 123 456",
            Direccion = Direccion(departamento, latitud, longitud)
        };

    private static CrearDireccionRequest Direccion(
        string departamento, decimal? latitud = null, decimal? longitud = null) => new()
        {
            Calle = "Av. 18 de Julio",
            Numero = "1234",
            Apartamento = "502",
            Ciudad = "Montevideo",
            Departamento = departamento,
            Referencia = "Timbre 5",
            Latitud = latitud,
            Longitud = longitud
        };
}
