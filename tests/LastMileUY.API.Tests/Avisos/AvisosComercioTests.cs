namespace LastMileUY.API.Tests.Avisos;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Notificaciones.Application;
using LastMileUY.API.Modules.Notificaciones.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Panel de avisos del comercio (historial y reenvío de fallidos), contra la base real con RLS.
// La base es compartida entre los tests: cada uno crea sus propios avisos.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AvisosComercioTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_comercio_ve_sus_avisos_y_no_los_de_otro()
    {
        var suyo = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Fallido);
        var ajeno = await CrearAvisoAsync(ComercioSoloDeA, EnvioDeAComercio2, EstadoEntregaAviso.Fallido);

        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var avisos = (await Servicio(db, ComercioCompartido).ListarAsync(1, 100, null)).Items;

        Assert.Contains(avisos, a => a.Id == suyo && a.CodigoSeguimiento == "CODIGOA");
        Assert.DoesNotContain(avisos, a => a.Id == ajeno);
    }

    [Fact]
    public async Task Se_pueden_filtrar_por_estado()
    {
        var fallido = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Fallido);
        var entregado = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Entregado);

        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var fallidos = (await Servicio(db, ComercioCompartido).ListarAsync(1, 100, "Fallido")).Items;

        Assert.Contains(fallidos, a => a.Id == fallido);
        Assert.DoesNotContain(fallidos, a => a.Id == entregado);
        Assert.All(fallidos, a => Assert.Equal("Fallido", a.Estado));
    }

    [Fact]
    public async Task Cada_aviso_dice_a_que_estado_paso_el_envio()
    {
        var cambio = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Entregado,
            contenido: """{"Estado": "EnDeposito", "EstadoAnterior": "Admitido"}""");
        var nuevo = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Entregado,
            contenido: """{"Estado": "Admitido", "EstadoAnterior": null}""");

        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var avisos = (await Servicio(db, ComercioCompartido).ListarAsync(1, 100, null)).Items;

        var deCambio = Assert.Single(avisos, a => a.Id == cambio);
        Assert.Equal(("EnDeposito", "Admitido"), (deCambio.EstadoEnvio, deCambio.EstadoEnvioAnterior));

        var deNuevo = Assert.Single(avisos, a => a.Id == nuevo);
        Assert.Equal(("Admitido", (string?)null), (deNuevo.EstadoEnvio, deNuevo.EstadoEnvioAnterior));
    }

    [Fact]
    public async Task El_detalle_trae_los_intentos_en_orden()
    {
        var id = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Fallido, intentos: 3);

        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);
        var detalle = await Servicio(db, ComercioCompartido).ObtenerAsync(id);

        Assert.NotNull(detalle);
        Assert.Equal(new[] { 1, 2, 3 }, detalle.Intentos.Select(i => i.NumeroIntento));
        Assert.Equal("https://tienda.uy/avisos/panel", detalle.Aviso.UrlDestino);
    }

    [Fact]
    public async Task Reenviar_un_fallido_lo_deja_pendiente_para_mandarlo_enseguida()
    {
        var id = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Fallido, intentos: 6);

        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido))
        {
            var reenviado = await Servicio(db, ComercioCompartido).ReenviarAsync(id);
            Assert.Equal("Pendiente", reenviado!.Estado);
        }

        await using var otra = baseDeDatos.CrearContexto(OperadorA);
        var aviso = await otra.EntregasAvisos.SingleAsync(a => a.Id == id);
        Assert.Equal(EstadoEntregaAviso.Pendiente, aviso.Estado);
        Assert.Null(aviso.FechaProximoIntento);
        // El historial no se pierde: el reenvío va a ser el intento 7
        Assert.Equal(6, aviso.CantidadIntentos);
    }
    
    
    [Fact]
    public async Task Dos_reenvios_concurrentes_solo_permiten_uno()
    {
        var id = await CrearAvisoAsync(
            ComercioCompartido,
            EnvioDeA,
            EstadoEntregaAviso.Fallido,
            intentos: 3);

        // Cada solicitud usa su propio DbContext,
        // como ocurre con dos peticiones HTTP independientes.
        await using var db1 = baseDeDatos.CrearContexto(
            OperadorA, ComercioCompartido);

        await using var db2 = baseDeDatos.CrearContexto(
            OperadorA, ComercioCompartido);

        var servicio1 = Servicio(db1, ComercioCompartido);
        var servicio2 = Servicio(db2, ComercioCompartido);

        // La barrera hace que ambas tareas comiencen juntas.
        var inicio = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> IntentarReenvioAsync(
            AvisosComercioService servicio)
        {
            await inicio.Task;

            try
            {
                return await servicio.ReenviarAsync(id) is not null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var tarea1 = IntentarReenvioAsync(servicio1);
        var tarea2 = IntentarReenvioAsync(servicio2);

        inicio.SetResult(true);

        var resultados = await Task.WhenAll(tarea1, tarea2);

        // Exactamente una solicitud debe aceptar el reenvío.
        Assert.Single(resultados.Where(x => x));

        // La otra solicitud debe ser rechazada.
        Assert.Single(resultados.Where(x => !x));

        // Comprobamos el estado final en la base de datos.
        await using var verificacion = baseDeDatos.CrearContexto(OperadorA);

        var aviso = await verificacion.EntregasAvisos
            .SingleAsync(a => a.Id == id);

        Assert.Equal(EstadoEntregaAviso.Pendiente, aviso.Estado);
        Assert.Null(aviso.FechaProximoIntento);

        // El reenvío no crea todavía un intento de entrega.
        // Ese intento lo registrará el worker.
        Assert.Equal(3, aviso.CantidadIntentos);
    }


    [Theory]
    [InlineData(EstadoEntregaAviso.Pendiente)]
    [InlineData(EstadoEntregaAviso.Entregado)]
    public async Task Solo_se_reenvian_los_fallidos(EstadoEntregaAviso estado)
    {
        var id = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, estado);

        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(db, ComercioCompartido).ReenviarAsync(id));
    }

    [Fact]
    public async Task No_se_reenvia_si_la_suscripcion_esta_desactivada()
    {
        var id = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Fallido, suscripcionActiva: false);

        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(db, ComercioCompartido).ReenviarAsync(id));
    }

    [Fact]
    public async Task Otro_comercio_no_puede_ver_ni_reenviar_un_aviso_ajeno()
    {
        var id = await CrearAvisoAsync(ComercioCompartido, EnvioDeA, EstadoEntregaAviso.Fallido);

        await using (var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA))
        {
            var servicio = Servicio(db, ComercioSoloDeA);
            Assert.Null(await servicio.ObtenerAsync(id));
            Assert.Null(await servicio.ReenviarAsync(id));
        }

        await using var otra = baseDeDatos.CrearContexto(OperadorA);
        Assert.Equal(EstadoEntregaAviso.Fallido, (await otra.EntregasAvisos.SingleAsync(a => a.Id == id)).Estado);
    }

    [Fact]
    public async Task Un_usuario_sin_comercio_no_puede_usar_el_panel()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new AvisosComercioService(db, new ContextoOperadorFijo(OperadorA)).ListarAsync(1, 20, null));
    }

    private static AvisosComercioService Servicio(ApplicationDbContext db, int comercioId) =>
        new(db, new ContextoOperadorFijo(OperadorA, comercioId));

    // Un aviso de un envío del operador A, con su suscripción y "intentos" filas de historial
    private async Task<int> CrearAvisoAsync(
        int comercioId, int envioId, EstadoEntregaAviso estado, int intentos = 1, bool suscripcionActiva = true,
        string contenido = "{}")
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, comercioId);

        var suscripcion = new SuscripcionAvisos
        {
            OperadorId = OperadorA,
            ComercioId = comercioId,
            TipoEvento = TiposEvento.EnvioEstadoCambiado,
            UrlDestino = "https://tienda.uy/avisos/panel",
            Activa = suscripcionActiva
        };
        db.SuscripcionesAvisos.Add(suscripcion);
        await db.SaveChangesAsync();

        var aviso = new EntregaAviso
        {
            SuscripcionAvisosId = suscripcion.Id,
            EnvioId = envioId,
            TipoEvento = TiposEvento.EnvioEstadoCambiado,
            Contenido = contenido,
            Estado = estado,
            CantidadIntentos = intentos,
            FechaUltimoIntento = DateTime.UtcNow,
            UltimoError = estado == EstadoEntregaAviso.Fallido ? "Respondió 503" : null
        };
        for (var i = 1; i <= intentos; i++)
            aviso.Intentos.Add(new IntentoAviso { NumeroIntento = i, CodigoHttp = 503, Error = "Respondió 503" });

        db.EntregasAvisos.Add(aviso);
        await db.SaveChangesAsync();

        return aviso.Id;
    }
}
