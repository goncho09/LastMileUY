namespace LastMileUY.API.Tests.Avisos;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Notificaciones.Application;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.Extensions.Configuration;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Suscripciones del comercio a avisos, contra la base real con RLS.
// La base es compartida entre los tests: cada uno usa sus propias URLs.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class SuscripcionesAvisosTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_comercio_crea_una_suscripcion_y_solo_la_ve_el()
    {
        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA))
        {
            var creada = await Servicio(db, OperadorA, ComercioSoloDeA)
                .CrearAsync(Pedido("https://tienda.uy/avisos/1"));

            Assert.True(creada.Activa);
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA))
        {
            var suyas = await Servicio(db, OperadorA, ComercioSoloDeA).ListarAsync();
            Assert.Contains(suyas, s => s.UrlDestino == "https://tienda.uy/avisos/1");
        }

        // Otro comercio del mismo operador no la ve
        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido))
        {
            var ajenas = await Servicio(db, OperadorA, ComercioCompartido).ListarAsync();
            Assert.DoesNotContain(ajenas, s => s.UrlDestino == "https://tienda.uy/avisos/1");
        }
    }

    [Fact]
    public async Task La_suscripcion_queda_con_el_operador_elegido()
    {
        await using (var db = baseDeDatos.CrearContexto(OperadorB, ComercioCompartido))
        {
            await Servicio(db, OperadorB, ComercioCompartido).CrearAsync(Pedido("https://tienda.uy/avisos/2"));
        }

        // El mismo comercio, trabajando con el otro operador, no la ve
        await using var conA = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var conOperadorA = await Servicio(conA, OperadorA, ComercioCompartido).ListarAsync();

        Assert.DoesNotContain(conOperadorA, s => s.UrlDestino == "https://tienda.uy/avisos/2");
    }

    [Theory]
    [InlineData("no es una url")]
    [InlineData("http://tienda.uy/avisos")]
    [InlineData("https://localhost/avisos")]
    [InlineData("https://10.0.0.5/avisos")]
    [InlineData("https://usuario:clave@tienda.uy/avisos")]
    public async Task Rechaza_las_urls_que_no_se_pueden_usar(string url)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Servicio(db, OperadorA, ComercioSoloDeA).CrearAsync(Pedido(url)));
    }

    [Fact]
    public async Task Si_no_se_pide_version_queda_en_la_1()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);

        var creada = await Servicio(db, OperadorA, ComercioSoloDeA).CrearAsync(Pedido("https://tienda.uy/avisos/v1"));

        Assert.Equal(VersionesAvisos.V1, creada.Version);
    }

    [Fact]
    public async Task Los_eventos_de_bulto_requieren_la_version_2()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var servicio = Servicio(db, OperadorA, ComercioSoloDeA);

        var conV1 = Pedido("https://tienda.uy/avisos/bultos-v1");
        conV1.TipoEvento = TiposEvento.BultoEstadoCambiado;
        var error = await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearAsync(conV1));
        Assert.Contains("versión 2", error.Message);

        var conV2 = Pedido("https://tienda.uy/avisos/bultos-v2");
        conV2.TipoEvento = TiposEvento.BultoEstadoCambiado;
        conV2.Version = VersionesAvisos.V2;
        var creada = await servicio.CrearAsync(conV2);

        Assert.Equal((TiposEvento.BultoEstadoCambiado, VersionesAvisos.V2), (creada.TipoEvento, creada.Version));
        Assert.Contains(await servicio.ListarAsync(), s => s.Id == creada.Id && s.Version == VersionesAvisos.V2);
    }

    [Fact]
    public async Task Rechaza_una_version_que_no_existe()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var pedido = Pedido("https://tienda.uy/avisos/v9");
        pedido.Version = 9;

        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(db, OperadorA, ComercioSoloDeA).CrearAsync(pedido));
    }

    [Fact]
    public async Task Rechaza_un_tipo_de_evento_que_no_existe()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var pedido = Pedido("https://tienda.uy/avisos/3");
        pedido.TipoEvento = "envio.inventado";

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Servicio(db, OperadorA, ComercioSoloDeA).CrearAsync(pedido));
    }

    [Fact]
    public async Task No_deja_dos_suscripciones_activas_iguales()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var servicio = Servicio(db, OperadorA, ComercioSoloDeA);

        await servicio.CrearAsync(Pedido("https://tienda.uy/avisos/4"));

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearAsync(Pedido("https://tienda.uy/avisos/4")));
    }

    [Fact]
    public async Task Se_puede_desactivar_y_volver_a_activar()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var servicio = Servicio(db, OperadorA, ComercioSoloDeA);
        var creada = await servicio.CrearAsync(Pedido("https://tienda.uy/avisos/5"));

        var desactivada = await servicio.CambiarActivaAsync(creada.Id, false);
        var activada = await servicio.CambiarActivaAsync(creada.Id, true);

        Assert.False(desactivada!.Activa);
        Assert.True(activada!.Activa);
    }

    [Fact]
    public async Task Otro_comercio_no_puede_cambiar_una_suscripcion_ajena()
    {
        int id;
        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA))
        {
            id = (await Servicio(db, OperadorA, ComercioSoloDeA).CrearAsync(Pedido("https://tienda.uy/avisos/6"))).Id;
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido))
        {
            // Para él no existe
            Assert.Null(await Servicio(db, OperadorA, ComercioCompartido).CambiarActivaAsync(id, false));
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA))
        {
            var suyas = await Servicio(db, OperadorA, ComercioSoloDeA).ListarAsync();
            Assert.True(Assert.Single(suyas, s => s.Id == id).Activa);
        }
    }

    [Fact]
    public async Task Un_usuario_sin_comercio_no_puede_crear_suscripciones()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Servicio(db, OperadorA, null).CrearAsync(Pedido("https://tienda.uy/avisos/7")));
    }

    [Fact]
    public async Task En_desarrollo_acepta_el_receptor_de_prueba()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);

        var creada = await Servicio(db, OperadorA, ComercioSoloDeA, permitirLocales: true)
            .CrearAsync(Pedido("http://lastmileuy.receptoravisos:8080/avisos"));

        Assert.Equal("http://lastmileuy.receptoravisos:8080/avisos", creada.UrlDestino);
    }

    private static SuscripcionesAvisosService Servicio(
        ApplicationDbContext db, int operadorId, int? comercioId, bool permitirLocales = false)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Avisos:PermitirDestinosLocales"] = permitirLocales.ToString()
            })
            .Build();

        return new SuscripcionesAvisosService(db, new ContextoOperadorFijo(operadorId, comercioId), configuracion);
    }

    private static CrearSuscripcionAvisosRequest Pedido(string url) => new()
    {
        TipoEvento = TiposEvento.EnvioEstadoCambiado,
        UrlDestino = url
    };
}
