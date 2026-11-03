namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Sincronizacion;
using static ApiDePuntaAPunta;

// Reglas de entrega parcial por HTTP: quién las ve y las publica, y que cada operación
// sincronizada diga con qué versión se evaluó. Lo que se publica acá permite las parciales,
// para no cambiarles el comportamiento a las otras pruebas que comparten la API.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ReglasEntregaParcialDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task El_operador_publica_una_version_nueva_y_queda_primera_en_el_historial()
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        // Los datos de prueba ya traen la versión 1
        var antes = (await operador.GetFromJsonAsync<List<ReglaEntregaParcialDto>>("api/operador/reglas-entrega-parcial"))!;
        Assert.NotEmpty(antes);

        using var respuesta = await operador.PostAsJsonAsync("api/operador/reglas-entrega-parcial",
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = true,
                DestinoBultosPendientes = "Devolucion",
                Motivo = "Los bultos que quedan vuelven al comercio"
            });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var publicada = (await respuesta.Content.ReadFromJsonAsync<ReglaEntregaParcialDto>())!;
        Assert.Equal(antes[0].Version + 1, publicada.Version);
        Assert.NotEqual("—", publicada.PublicadaPor);

        var despues = (await operador.GetFromJsonAsync<List<ReglaEntregaParcialDto>>("api/operador/reglas-entrega-parcial"))!;
        Assert.Equal(publicada.Id, despues[0].Id);
        Assert.Equal(antes.Count + 1, despues.Count);
    }

    [Theory]
    [InlineData("Ninguno", "Un motivo")]
    [InlineData("Devolucion", "")]
    public async Task Sin_un_destino_valido_o_sin_motivo_es_un_400(string destino, string motivo)
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await operador.PostAsJsonAsync("api/operador/reglas-entrega-parcial",
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = true,
                DestinoBultosPendientes = destino,
                Motivo = motivo
            });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    // Es configuración del operador: el despachador y el repartidor no la ven ni la cambian
    [Theory]
    [InlineData(Despachador1)]
    [InlineData(Repartidor1)]
    public async Task Solo_el_perfil_operador_ve_y_publica_las_reglas(string email)
    {
        var cliente = await api.IniciarSesionAsync(email);

        using var ver = await cliente.GetAsync("api/operador/reglas-entrega-parcial");
        using var publicar = await cliente.PostAsJsonAsync("api/operador/reglas-entrega-parcial",
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = true,
                DestinoBultosPendientes = "NuevaHojaDeRuta",
                Motivo = "Intento sin permiso"
            });

        Assert.Equal(HttpStatusCode.Forbidden, ver.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, publicar.StatusCode);
    }

    [Fact]
    public async Task Cada_operacion_sincronizada_dice_con_que_version_se_evaluo()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);

        // La ruta sale con la versión vigente; después se publica otra, que no la cambia
        var vigente = (await operador.GetFromJsonAsync<List<ReglaEntregaParcialDto>>("api/operador/reglas-entrega-parcial"))![0];
        var (rutaId, envioId) = await RutasDePuntaAPunta.RutaDespachadaAsync(api, operador, despachador, diasAdelante: 30);

        using (var otra = await operador.PostAsJsonAsync("api/operador/reglas-entrega-parcial",
                   new PublicarReglaEntregaParcialRequest
                   {
                       PermiteEntregaParcial = true,
                       DestinoBultosPendientes = "NuevaHojaDeRuta",
                       Motivo = "Vuelven a salir en otra hoja de ruta"
                   }))
        {
            Assert.Equal(HttpStatusCode.Created, otra.StatusCode);
        }

        var resultado = await SincronizacionDePuntaAPuntaTests.SincronizarAsync(repartidor, new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(),
            Tipo = "Entrega",
            RutaId = rutaId,
            EnvioId = envioId,
            FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-5),
            Latitud = -34.9m,
            Longitud = -56.16m,
            NombreReceptor = "Ana Silva",
            FirmaBase64 = Png
        });

        Assert.Equal("Aplicada", resultado.Resultado);

        var operaciones = (await despachador.GetFromJsonAsync<PaginaDto<OperacionSincronizadaDto>>(
            $"api/sincronizacion/operaciones?rutaId={rutaId}"))!;

        Assert.Equal(vigente.Version, Assert.Single(operaciones.Items).VersionReglasEntregaParcial);
    }
}
