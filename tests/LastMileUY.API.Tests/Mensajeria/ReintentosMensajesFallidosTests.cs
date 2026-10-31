
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Mensajeria.Application;
using LastMileUY.API.Modules.Mensajeria.Domain;
using LastMileUY.API.Tests.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Tests.Mensajeria;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ReintentosMensajesFallidosTests(
    BaseConDosOperadores baseDeDatos)
{
    private ApplicationDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(baseDeDatos.CadenaAdministrador)
            .Options;

        return new ApplicationDbContext(
            opciones,
            new ContextoOperadorFijo(null));
    }

    [Fact]
    public async Task Reintentar_pendiente_crea_outbox_y_actualiza_estado()
    {
        await using var db = CrearContexto();

        var mensaje = new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            Estado = EstadoMensajeFallido.Pendiente
        };

        db.MensajesFallidos.Add(mensaje);
        await db.SaveChangesAsync();

        var servicio = new ReintentosMensajesFallidosService(db);

        var resultado = await servicio.SolicitarAsync(
            mensaje.Id, "admin-test");

        Assert.True(resultado);

        await db.Entry(mensaje).ReloadAsync();

        Assert.Equal(
            EstadoMensajeFallido.ReprocesamientoSolicitado,
            mensaje.Estado);

        Assert.Equal("admin-test", mensaje.SolicitadoPorId);
        Assert.NotNull(mensaje.FechaSolicitudReproceso);

        var outbox = await db.MensajesReintentoOutbox
            .SingleAsync(x => x.MensajeFallidoId == mensaje.Id);

        Assert.Equal(mensaje.MensajeId, outbox.MensajeId);
        Assert.Equal(mensaje.Contenido, outbox.Contenido);
        Assert.Null(outbox.FechaPublicacion);
    }

    [Fact]
    public async Task Reintentar_dos_veces_no_duplica_outbox()
    {
        await using var db = CrearContexto();

        var mensaje = new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}"
        };

        db.MensajesFallidos.Add(mensaje);
        await db.SaveChangesAsync();

        var servicio = new ReintentosMensajesFallidosService(db);

        Assert.True(await servicio.SolicitarAsync(
            mensaje.Id, "admin-test"));

        Assert.False(await servicio.SolicitarAsync(
            mensaje.Id, "admin-test"));

        var cantidad = await db.MensajesReintentoOutbox
            .CountAsync(x => x.MensajeFallidoId == mensaje.Id);

        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Reintentar_resuelto_es_rechazado()
    {
        await using var db = CrearContexto();

        var mensaje = new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            Estado = EstadoMensajeFallido.Resuelto
        };

        db.MensajesFallidos.Add(mensaje);
        await db.SaveChangesAsync();

        var servicio = new ReintentosMensajesFallidosService(db);

        Assert.False(await servicio.SolicitarAsync(
            mensaje.Id, "admin-test"));

        Assert.False(await db.MensajesReintentoOutbox
            .AnyAsync(x => x.MensajeFallidoId == mensaje.Id));
    }
}
