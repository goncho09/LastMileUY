extern alias AppRepartidor;

namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using AppRepartidor::LastMileUY.DriverPWA.Services;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.JSInterop;
using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ModoSinConexionDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task Una_jornada_sin_senal_llega_entera_y_en_orden_al_volver_la_conexion()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        await PermitirEntregasParcialesAsync(operador);

        var (rutaId, envios) = await RutasDePuntaAPunta.RutaConEnviosAsync(
            api, operador, despachador, diasAdelante: 50, cantidadEnvios: 4, bultosPorEnvio: 2);
        await RutasDePuntaAPunta.DespacharAsync(despachador, rutaId);
        var (completa, parcial, fallida) = (envios[0], envios[1], envios[2]);

        var celular = new Celular(api);
        var app = celular.Abrir();
        Assert.True(await app.Auth.LoginAsync(Repartidor1, Clave));
        Assert.Contains(await app.Rutas.ObtenerMisRutasAsync(), r => r.Id == rutaId);
        Assert.NotNull(await app.Rutas.ObtenerRutaAsync(rutaId));
        var motivos = await app.Sincronizacion.ObtenerMotivosAsync();
        Assert.NotEmpty(motivos);

        celular.Red.Cortar();
        app = celular.Abrir();
        var hoja = (await app.Rutas.ObtenerRutaAsync(rutaId))!;
        Assert.Equal(motivos, await app.Sincronizacion.ObtenerMotivosAsync());
        var bultos = hoja.Paradas.ToDictionary(p => p.EnvioId, p => p.Bultos!.ToList());
        Assert.All(envios, id => Assert.Equal(2, bultos[id].Count));

        var hace30 = DateTime.UtcNow.AddMinutes(-30);
        var hace20 = DateTime.UtcNow.AddMinutes(-20);
        var hace10 = DateTime.UtcNow.AddMinutes(-10);

        await app.Bultos.GuardarAsync(rutaId, completa, [.. bultos[completa].Select(b => b.Id)]);
        var decision = await DecidirAsync(app, rutaId, completa, bultos[completa], hoja, motivoDelResto: null);
        Assert.Equal("Entrega", decision.Tipo);
        var estado = await app.Sincronizacion.GuardarOperacionAsync(
            Entrega(decision, rutaId, completa, hace30));
        Assert.True(estado.Pendiente);

        var entregado = bultos[parcial][0];
        var noEntregado = bultos[parcial][1];
        await app.Bultos.GuardarAsync(rutaId, parcial, [entregado.Id]);
        decision = await DecidirAsync(app, rutaId, parcial, bultos[parcial], hoja, motivos[0].Id);
        Assert.Equal("EntregaParcial", decision.Tipo);
        Assert.True((await app.Sincronizacion.GuardarOperacionAsync(
            Entrega(decision, rutaId, parcial, hace20))).Pendiente);

        Assert.True((await app.Sincronizacion.GuardarOperacionAsync(new OperacionRequest
        {
            Tipo = "IntentoFallido",
            RutaId = rutaId,
            EnvioId = fallida,
            FechaHoraDispositivo = hace10,
            Latitud = -34.9m,
            Longitud = -56.16m,
            MotivoNoEntregaId = motivos[0].Id
        })).Pendiente);

        Assert.True(celular.Red.PedidosSinSalir > 0);
        foreach (var id in envios)
            Assert.Equal("EnTransito", (await DetalleAsync(operador, id)).Estado);

        app = celular.Abrir();
        Assert.Equal(3, (await app.Cola.ObtenerPendientesAsync()).Count);
        var guardada = (await app.Sincronizacion.ObtenerPendienteAsync(parcial))!;
        Assert.Equal(
            [new ResultadoBultoRequest(entregado.Id, true), new ResultadoBultoRequest(noEntregado.Id, false, motivos[0].Id)],
            guardada.ResultadosBultos!);

        celular.Red.Conectar();
        Assert.Equal((3, 0), await app.Sincronizacion.SincronizarPendientesAsync());

        var mias = (await app.Sincronizacion.ObtenerMisOperacionesAsync(null, 1)).Items
            .Where(o => o.RutaId == rutaId)
            .OrderBy(o => o.FechaRecepcion)
            .ToList();
        Assert.Equal([completa, parcial, fallida], mias.Select(o => o.EnvioId));
        Assert.All(mias, o => Assert.Equal("Aplicada", o.Resultado));

        var detalleCompleta = await DetalleAsync(operador, completa);
        Assert.Equal("Entregado", detalleCompleta.Estado);
        Assert.All(detalleCompleta.Bultos, b => Assert.Equal("Entregado", b.Estado));
        Assert.Equal(hace30, Assert.Single(detalleCompleta.Intentos).FechaHora, TimeSpan.FromSeconds(1));

        var detalleParcial = await DetalleAsync(operador, parcial);
        var intentoParcial = Assert.Single(detalleParcial.Intentos);
        Assert.Equal("Parcial", intentoParcial.Resultado);
        Assert.Equal(hace20, intentoParcial.FechaHora, TimeSpan.FromSeconds(1));
        Assert.Equal("Entregado", detalleParcial.Bultos.Single(b => b.Codigo == entregado.Codigo).Estado);
        Assert.Equal("NoEntregado", detalleParcial.Bultos.Single(b => b.Codigo == noEntregado.Codigo).Estado);
        Assert.Equal([entregado.Codigo], intentoParcial.Prueba!.BultosEntregados);

        var intentoFallido = Assert.Single((await DetalleAsync(operador, fallida)).Intentos);
        Assert.Equal("NoEntregado", intentoFallido.Resultado);
        Assert.Equal(hace10, intentoFallido.FechaHora, TimeSpan.FromSeconds(1));

        Assert.Empty(await app.Cola.ObtenerPendientesAsync());
        Assert.True(await app.Confirmadas.EstaConfirmadoAsync(completa));
        celular.Red.Cortar();
        var hojaGuardada = (await celular.Abrir().Rutas.ObtenerRutaAsync(rutaId))!;
        Assert.Equal("Entregado", hojaGuardada.Paradas.Single(p => p.EnvioId == completa).EstadoEnvio);
        Assert.Equal("EnTransito", hojaGuardada.Paradas.Single(p => p.EnvioId == envios[3]).EstadoEnvio);
    }

    [Fact]
    public async Task Si_se_pierde_la_respuesta_el_reenvio_no_aplica_la_entrega_dos_veces()
    {
        var (operador, rutaId, envioId) = await RutaDeUnEnvioAsync(diasAdelante: 51);
        var celular = new Celular(api);
        var app = celular.Abrir();
        Assert.True(await app.Auth.LoginAsync(Repartidor1, Clave));
        var hoja = (await app.Rutas.ObtenerRutaAsync(rutaId))!;
        var parada = Assert.Single(hoja.Paradas);

        celular.Red.PerderRespuestas();
        var decision = await DecidirAsync(app, rutaId, envioId, parada.Bultos!, hoja, motivoDelResto: null, escanearTodos: true);
        Assert.True((await app.Sincronizacion.GuardarOperacionAsync(Entrega(decision, rutaId, envioId, DateTime.UtcNow))).Pendiente);
        Assert.Equal("Entregado", (await DetalleAsync(operador, envioId)).Estado);

        celular.Red.Conectar();
        var pendiente = Assert.Single(await app.Cola.ObtenerPendientesAsync());
        var estado = await app.Sincronizacion.ReintentarOperacionAsync(pendiente.IdOperacion);

        Assert.False(estado.Pendiente);
        Assert.True(estado.Resultado!.Duplicada);
        Assert.Equal("Aplicada", estado.Resultado.Resultado);
        Assert.Empty(await app.Cola.ObtenerPendientesAsync());
        Assert.Single((await DetalleAsync(operador, envioId)).Intentos);
    }

    [Fact]
    public async Task Si_el_backoffice_cambio_el_envio_mientras_tanto_manda_el_servidor()
    {
        var (operador, rutaId, envioId) = await RutaDeUnEnvioAsync(diasAdelante: 52);
        var celular = new Celular(api);
        var app = celular.Abrir();
        Assert.True(await app.Auth.LoginAsync(Repartidor1, Clave));
        var hoja = (await app.Rutas.ObtenerRutaAsync(rutaId))!;

        celular.Red.Cortar();
        var decision = await DecidirAsync(app, rutaId, envioId, hoja.Paradas[0].Bultos!, hoja, motivoDelResto: null, escanearTodos: true);
        Assert.True((await app.Sincronizacion.GuardarOperacionAsync(Entrega(decision, rutaId, envioId, DateTime.UtcNow))).Pendiente);

        using (var cambio = await operador.PatchAsJsonAsync($"api/envios/{envioId}/estado",
                   new CambiarEstadoRequest { Estado = "Extraviado", Descripcion = "No aparece en el camión" }))
        {
            Assert.True(cambio.IsSuccessStatusCode);
        }

        celular.Red.Conectar();
        var pendiente = Assert.Single(await app.Cola.ObtenerPendientesAsync());
        var estado = await app.Sincronizacion.ReintentarOperacionAsync(pendiente.IdOperacion);

        Assert.False(estado.Pendiente);
        Assert.Equal("Conflicto", estado.Resultado!.Resultado);
        Assert.Equal("Extraviado", estado.Resultado.EstadoEnvio);
        Assert.Empty(await app.Cola.ObtenerPendientesAsync());
        Assert.False(await app.Confirmadas.EstaConfirmadoAsync(envioId));

        var detalle = await DetalleAsync(operador, envioId);
        Assert.Equal("Extraviado", detalle.Estado);
        Assert.Empty(detalle.Intentos);

        var conflictos = await app.Sincronizacion.ObtenerMisOperacionesAsync("Conflicto", 1);
        Assert.Contains(conflictos.Items, o => o.IdOperacion == pendiente.IdOperacion);
    }

    [Fact]
    public async Task Si_la_sesion_vencio_lo_pendiente_no_se_pierde_y_sale_al_volver_a_entrar()
    {
        var (operador, rutaId, envioId) = await RutaDeUnEnvioAsync(diasAdelante: 53);
        var celular = new Celular(api);
        var app = celular.Abrir();
        Assert.True(await app.Auth.LoginAsync(Repartidor1, Clave));
        var hoja = (await app.Rutas.ObtenerRutaAsync(rutaId))!;

        celular.Red.Cortar();
        var decision = await DecidirAsync(app, rutaId, envioId, hoja.Paradas[0].Bultos!, hoja, motivoDelResto: null, escanearTodos: true);
        Assert.True((await app.Sincronizacion.GuardarOperacionAsync(Entrega(decision, rutaId, envioId, DateTime.UtcNow))).Pendiente);

        celular.PerderSesion();
        celular.Red.Conectar();
        app = celular.Abrir();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.Sincronizacion.SincronizarPendientesAsync());
        Assert.Single(await app.Cola.ObtenerPendientesAsync());

        Assert.True(await app.Auth.LoginAsync(Repartidor1, Clave));
        Assert.Equal((1, 0), await app.Sincronizacion.SincronizarPendientesAsync());
        Assert.Equal("Entregado", (await DetalleAsync(operador, envioId)).Estado);
    }

    private static async Task<ResultadoPorBulto.Decision> DecidirAsync(
        App app, int rutaId, int envioId, IReadOnlyList<BultoRutaDto> bultos, RutaDetalleDto hoja,
        int? motivoDelResto, bool escanearTodos = false)
    {
        if (escanearTodos)
            await app.Bultos.GuardarAsync(rutaId, envioId, [.. bultos.Select(b => b.Id)]);

        var identificados = (await app.Bultos.LeerAsync(rutaId)).GetValueOrDefault(envioId) ?? [];
        var marcas = ResultadoPorBulto.MarcasIniciales(bultos, identificados);

        foreach (var marca in marcas.Values.Where(m => !m.Entregado))
            marca.MotivoNoEntregaId = motivoDelResto;

        var decision = ResultadoPorBulto.Decidir(bultos, marcas, hoja.ReglaEntregaParcial);
        Assert.Null(decision.Error);

        return decision;
    }

    private static OperacionRequest Entrega(ResultadoPorBulto.Decision decision, int rutaId, int envioId, DateTime cuando) => new()
    {
        Tipo = decision.Tipo,
        ResultadosBultos = decision.Resultados,
        RutaId = rutaId,
        EnvioId = envioId,
        FechaHoraDispositivo = cuando,
        Latitud = -34.9m,
        Longitud = -56.16m,
        NombreReceptor = "Ana Silva",
        FirmaBase64 = Png
    };

    private async Task<(HttpClient Operador, int RutaId, int EnvioId)> RutaDeUnEnvioAsync(int diasAdelante)
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var (rutaId, envioId) = await RutasDePuntaAPunta.RutaDespachadaAsync(api, operador, despachador, diasAdelante);

        return (operador, rutaId, envioId);
    }

    private static async Task PermitirEntregasParcialesAsync(HttpClient operador)
    {
        using var respuesta = await operador.PostAsJsonAsync("api/operador/reglas-entrega-parcial",
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = true,
                DestinoBultosPendientes = "NuevaHojaDeRuta",
                Motivo = "Prueba sin conexión"
            });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
    }

    private static async Task<EnvioDetalleDto> DetalleAsync(HttpClient operador, int envioId) =>
        (await operador.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{envioId}"))!;

    private sealed class Celular
    {
        private readonly ApiDePuntaAPunta api;
        private readonly NavegadorEnMemoria navegador = new();
        private HttpClient http;

        public Celular(ApiDePuntaAPunta api)
        {
            this.api = api;
            http = Cliente();
        }

        public Red Red { get; private set; } = new();

        public App Abrir() => new(http, navegador);

        public void PerderSesion() => http = Cliente();

        private HttpClient Cliente() =>
            api.CreateDefaultClient(new Uri("https://localhost/"), new RedDelCelular(this), new CookieContainerHandler());

        private sealed class RedDelCelular(Celular celular) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                celular.Red.EnviarAsync(request, r => base.SendAsync(r, cancellationToken));
        }
    }

    private sealed class App
    {
        public App(HttpClient http, IJSRuntime navegador)
        {
            var almacen = new AlmacenLocal(navegador, TimeProvider.System);
            Cola = new ColaOperacionesService(almacen);
            Confirmadas = new OperacionesConfirmadasService(almacen);
            Rutas = new RutasService(http, almacen);
            Bultos = new BultosIdentificadosService(almacen);
            Sincronizacion = new SincronizacionService(http, almacen, Rutas, Confirmadas, Cola);
            Auth = new AuthService(http, almacen, Cola);
        }

        public ColaOperacionesService Cola { get; }
        public OperacionesConfirmadasService Confirmadas { get; }
        public RutasService Rutas { get; }
        public BultosIdentificadosService Bultos { get; }
        public SincronizacionService Sincronizacion { get; }
        public AuthService Auth { get; }
    }

    private sealed class Red
    {
        private enum Modo { Conectada, Cortada, PierdeRespuestas }

        private Modo modo = Modo.Conectada;

        public int PedidosSinSalir { get; private set; }

        public void Conectar() => modo = Modo.Conectada;
        public void Cortar() => modo = Modo.Cortada;
        public void PerderRespuestas() => modo = Modo.PierdeRespuestas;

        public async Task<HttpResponseMessage> EnviarAsync(
            HttpRequestMessage request, Func<HttpRequestMessage, Task<HttpResponseMessage>> enviar)
        {
            if (modo == Modo.Cortada)
            {
                PedidosSinSalir++;
                throw new HttpRequestException("Sin conexión");
            }

            var respuesta = await enviar(request);

            if (modo == Modo.PierdeRespuestas)
            {
                respuesta.Dispose();
                throw new HttpRequestException("Se cortó la conexión antes de la respuesta");
            }

            return respuesta;
        }
    }

    private sealed class NavegadorEnMemoria : IJSRuntime
    {
        private readonly Dictionary<string, string> datos = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            object? resultado = null;

            switch (identifier)
            {
                case "lastMileAlmacen.guardar":
                    datos[(string)args![0]!] = (string)args[1]!;
                    break;
                case "lastMileAlmacen.leer":
                    resultado = datos.GetValueOrDefault((string)args![0]!);
                    break;
                case "lastMileAlmacen.borrar":
                    datos.Remove((string)args![0]!);
                    break;
                case "lastMileAlmacen.borrarTodo":
                    datos.Clear();
                    break;
                default:
                    throw new InvalidOperationException($"Función inesperada: {identifier}");
            }

            return ValueTask.FromResult((TValue)resultado!);
        }
    }
}
