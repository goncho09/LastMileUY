using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Configuracion;
using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Rutas;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ConfiguracionOperadorDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static string Unico(string prefijo) => $"{prefijo} {Guid.NewGuid():N}"[..Math.Min(prefijo.Length + 9, 40)];

    [Fact]
    public async Task El_operador_crea_una_zona_con_franjas_y_otro_operador_no_la_ve()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var otro = await api.IniciarSesionAsync(Operador2);

        using var creada = await operador.PostAsJsonAsync("api/operador/zonas", new GuardarZonaRequest(Unico("Centro"), "Centro y Cordón"));
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
        var zona = (await creada.Content.ReadFromJsonAsync<ZonaDto>())!;

        using var franja = await operador.PostAsJsonAsync($"api/operador/zonas/{zona.Id}/franjas",
            new GuardarFranjaHorariaRequest("Mañana", new TimeOnly(9, 0), new TimeOnly(13, 0)));
        Assert.Equal(HttpStatusCode.Created, franja.StatusCode);

        var leida = (await operador.GetFromJsonAsync<ZonaDto>($"api/operador/zonas/{zona.Id}"))!;
        Assert.Equal("Mañana", Assert.Single(leida.Franjas).Nombre);

        using var ajena = await otro.GetAsync($"api/operador/zonas/{zona.Id}");
        using var editarAjena = await otro.PutAsJsonAsync($"api/operador/zonas/{zona.Id}", new GuardarZonaRequest("Robada", null));
        var listaAjena = (await otro.GetFromJsonAsync<List<ZonaDto>>("api/operador/zonas"))!;

        Assert.Equal(HttpStatusCode.NotFound, ajena.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editarAjena.StatusCode);
        Assert.DoesNotContain(listaAjena, z => z.Id == zona.Id);
    }

    [Fact]
    public async Task Una_zona_repetida_es_un_409_y_una_franja_al_reves_es_un_400()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var nombre = Unico("Norte");

        using var primera = await operador.PostAsJsonAsync("api/operador/zonas", new GuardarZonaRequest(nombre, null));
        using var repetida = await operador.PostAsJsonAsync("api/operador/zonas", new GuardarZonaRequest(nombre, null));
        var zona = (await primera.Content.ReadFromJsonAsync<ZonaDto>())!;

        using var alReves = await operador.PostAsJsonAsync($"api/operador/zonas/{zona.Id}/franjas",
            new GuardarFranjaHorariaRequest("Tarde", new TimeOnly(18, 0), new TimeOnly(14, 0)));

        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, alReves.StatusCode);
    }

    [Fact]
    public async Task Un_vehiculo_nuevo_aparece_para_armar_rutas_y_dado_de_baja_desaparece()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var matricula = $"T{Random.Shared.Next(100000, 999999)}";

        using var creado = await operador.PostAsJsonAsync("api/operador/vehiculos",
            new GuardarVehiculoRequest(matricula, "Furgón", 800m, 6.5m));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var vehiculo = (await creado.Content.ReadFromJsonAsync<VehiculoConfiguracionDto>())!;

        using var repetido = await operador.PostAsJsonAsync("api/operador/vehiculos",
            new GuardarVehiculoRequest(matricula.ToLowerInvariant(), "Moto", 20m, 0.1m));
        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);

        Assert.Contains(await OpcionesDeVehiculosAsync(operador), id => id == vehiculo.Id);

        using var baja = await operador.PutAsJsonAsync($"api/operador/vehiculos/{vehiculo.Id}/activo", new CambiarActivoRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);

        Assert.DoesNotContain(await OpcionesDeVehiculosAsync(operador), id => id == vehiculo.Id);
    }

    [Theory]
    [InlineData("", "Furgón", 100, 1)]
    [InlineData("ABC1234", "Furgón", 0, 1)]
    [InlineData("ABC1234", "Furgón", 100, -1)]
    public async Task Un_vehiculo_con_datos_invalidos_es_un_400(string matricula, string tipo, decimal peso, decimal volumen)
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await operador.PostAsJsonAsync("api/operador/vehiculos",
            new GuardarVehiculoRequest(matricula, tipo, peso, volumen));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_motivo_nuevo_le_llega_al_repartidor_y_desactivado_ya_no()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var nombre = Unico("Perro suelto");

        using var creado = await operador.PostAsJsonAsync("api/operador/motivos-no-entrega",
            new GuardarMotivoNoEntregaRequest(nombre, "No se pudo llegar a la puerta"));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var motivo = (await creado.Content.ReadFromJsonAsync<MotivoNoEntregaConfiguracionDto>())!;

        var paraElRepartidor = (await repartidor.GetFromJsonAsync<List<MotivoNoEntregaDto>>("api/sincronizacion/motivos"))!;
        Assert.Contains(paraElRepartidor, m => m.Id == motivo.Id);

        using var baja = await operador.PutAsJsonAsync($"api/operador/motivos-no-entrega/{motivo.Id}/activo", new CambiarActivoRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);

        paraElRepartidor = (await repartidor.GetFromJsonAsync<List<MotivoNoEntregaDto>>("api/sincronizacion/motivos"))!;
        Assert.DoesNotContain(paraElRepartidor, m => m.Id == motivo.Id);
    }

    [Fact]
    public async Task Activar_una_regla_desactiva_la_otra_de_la_misma_modalidad()
    {
        var operador = await api.IniciarSesionAsync(Operador2);
        var antes = (await operador.GetFromJsonAsync<List<ReglaOperativaConfiguracionDto>>("api/operador/reglas-operativas"))!;
        var original = antes.Single(r => r.Activa && r.Modalidad == "Urgente");

        try
        {
            using var creada = await operador.PostAsJsonAsync("api/operador/reglas-operativas",
                new GuardarReglaOperativaRequest(Unico("Urgente nueva"), null, "Urgente", 3, 2, 8, 12, true, true, false));
            Assert.Equal(HttpStatusCode.Created, creada.StatusCode);
            var nueva = (await creada.Content.ReadFromJsonAsync<ReglaOperativaConfiguracionDto>())!;

            var despues = (await operador.GetFromJsonAsync<List<ReglaOperativaConfiguracionDto>>("api/operador/reglas-operativas"))!;
            Assert.Equal(nueva.Id, Assert.Single(despues, r => r.Activa && r.Modalidad == "Urgente").Id);
            Assert.Contains(despues, r => r.Id == original.Id && !r.Activa);
        }
        finally
        {
            using var restaurar = await operador.PutAsJsonAsync($"api/operador/reglas-operativas/{original.Id}/activa", new CambiarActivoRequest(true));
            Assert.Equal(HttpStatusCode.NoContent, restaurar.StatusCode);
        }

        var final = (await operador.GetFromJsonAsync<List<ReglaOperativaConfiguracionDto>>("api/operador/reglas-operativas"))!;
        Assert.Equal(original.Id, Assert.Single(final, r => r.Activa && r.Modalidad == "Urgente").Id);
    }

    [Fact]
    public async Task Una_regla_con_modalidad_inexistente_es_un_400()
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await operador.PostAsJsonAsync("api/operador/reglas-operativas",
            new GuardarReglaOperativaRequest("Express", null, "Express", 1, 1, 1, 1, false, false, false));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_cuadro_programado_se_edita_y_uno_nuevo_cierra_la_vigencia_del_anterior()
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        using var zonaCreada = await operador.PostAsJsonAsync("api/operador/zonas", new GuardarZonaRequest(Unico("Tarifaria"), null));
        var zona = (await zonaCreada.Content.ReadFromJsonAsync<ZonaDto>())!;

        var manana = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);

        using var hoy = await operador.PostAsJsonAsync("api/operador/cuadros-tarifarios",
            new CrearCuadroTarifarioRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), false));
        Assert.Equal(HttpStatusCode.BadRequest, hoy.StatusCode);

        using var primero = await operador.PostAsJsonAsync("api/operador/cuadros-tarifarios", new CrearCuadroTarifarioRequest(manana, false));
        Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
        var cuadro = (await primero.Content.ReadFromJsonAsync<CuadroTarifarioDto>())!;
        Assert.True(cuadro.Editable);

        using var tarifa = await operador.PostAsJsonAsync($"api/operador/cuadros-tarifarios/{cuadro.Id}/tarifas",
            new GuardarTarifaRequest("Hasta 5 kg", zona.Id, "Estandar", 0m, 5m, 0m, 0.5m, 250m, 30m));
        Assert.Equal(HttpStatusCode.Created, tarifa.StatusCode);

        using var alReves = await operador.PostAsJsonAsync($"api/operador/cuadros-tarifarios/{cuadro.Id}/tarifas",
            new GuardarTarifaRequest("Mal", zona.Id, "Estandar", 10m, 5m, 0m, 0.5m, 250m, 30m));
        Assert.Equal(HttpStatusCode.BadRequest, alReves.StatusCode);

        using var segundo = await operador.PostAsJsonAsync("api/operador/cuadros-tarifarios", new CrearCuadroTarifarioRequest(manana.AddDays(10), true));
        Assert.Equal(HttpStatusCode.Created, segundo.StatusCode);
        var nuevo = (await segundo.Content.ReadFromJsonAsync<CuadroTarifarioDto>())!;

        Assert.Equal(cuadro.Version + 1, nuevo.Version);
        Assert.Equal("Hasta 5 kg", Assert.Single(nuevo.Tarifas).Nombre);

        var anterior = (await operador.GetFromJsonAsync<CuadroTarifarioDto>($"api/operador/cuadros-tarifarios/{cuadro.Id}"))!;
        Assert.Equal(manana.AddDays(10), anterior.VigenciaHasta);

        using var antesDelAnterior = await operador.PostAsJsonAsync("api/operador/cuadros-tarifarios", new CrearCuadroTarifarioRequest(manana.AddDays(5), false));
        Assert.Equal(HttpStatusCode.BadRequest, antesDelAnterior.StatusCode);
    }

    [Fact]
    public async Task Un_cuadro_que_ya_rige_no_se_puede_cambiar()
    {
        var operador = await api.IniciarSesionAsync(Operador2);

        using var zonaCreada = await operador.PostAsJsonAsync("api/operador/zonas", new GuardarZonaRequest(Unico("Vigente"), null));
        var zona = (await zonaCreada.Content.ReadFromJsonAsync<ZonaDto>())!;

        await api.EjecutarAsync(
            $"INSERT INTO \"CuadrosTarifarios\" (\"OperadorId\", \"Version\", \"VigenciaDesde\", \"FechaCreacion\") " +
            $"VALUES ({api.IdOperador2}, 900, now() - interval '1 day', now())");

        var vigente = (await operador.GetFromJsonAsync<List<CuadroTarifarioDto>>("api/operador/cuadros-tarifarios"))!
            .Single(c => c.Version == 900);
        Assert.Equal(EstadosCuadroTarifario.Vigente, vigente.Estado);

        using var respuesta = await operador.PostAsJsonAsync($"api/operador/cuadros-tarifarios/{vigente.Id}/tarifas",
            new GuardarTarifaRequest("Tarde", zona.Id, "Estandar", 0m, 5m, 0m, 0.5m, 250m, 30m));

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(Despachador1)]
    [InlineData(Repartidor1)]
    public async Task Solo_el_perfil_operador_entra_a_la_configuracion(string email)
    {
        var cliente = await api.IniciarSesionAsync(email);

        foreach (var ruta in new[] { "zonas", "vehiculos", "motivos-no-entrega", "reglas-operativas", "cuadros-tarifarios" })
        {
            using var respuesta = await cliente.GetAsync($"api/operador/{ruta}");
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        }
    }

    private static async Task<List<int>> OpcionesDeVehiculosAsync(HttpClient cliente)
    {
        var opciones = (await cliente.GetFromJsonAsync<OpcionesRutaDto>("api/rutas/opciones"))!;
        return opciones.Vehiculos.Select(v => v.Id).ToList();
    }
}
