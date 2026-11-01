
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Mensajeria.Application;
using LastMileUY.API.Modules.Mensajeria.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Tests.Mensajeria;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ConsultasMensajesFallidosTests(
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
public async Task Filtro_de_fechas_respeta_el_dia_de_Uruguay()
{
    await using var db = CrearContexto();

    // El 1/11 en Uruguay empieza a las 03:00 UTC.
    var inicio = new DateTime(
        2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);

    var mensajes = new[]
    {
        // 31/10 23:59:59 en Uruguay: fuera del rango.
        new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            FechaFallo = inicio.AddSeconds(-1)
        },

        // 01/11 00:00 en Uruguay: incluido.
        new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            FechaFallo = inicio
        },

        // 01/11 15:30 en Uruguay: incluido.
        new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            FechaFallo = inicio.AddHours(15).AddMinutes(30)
        },

        // 01/11 23:59:59 en Uruguay: incluido.
        new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            FechaFallo = inicio.AddDays(1).AddSeconds(-1)
        },

        // 02/11 00:00 en Uruguay: fuera del rango.
        new MensajeFallido
        {
            MensajeId = Guid.NewGuid().ToString(),
            Contenido = "{}",
            FechaFallo = inicio.AddDays(1)
        }
    };

    db.MensajesFallidos.AddRange(mensajes);
    await db.SaveChangesAsync();

    var servicio = new ConsultasMensajesFallidosService(db);

    // DateTime sin zona horaria, como llega desde Backoffice.
    var filtro = new FiltroMensajesFallidos
    {
        Desde = new DateTime(2026, 11, 1),
        Hasta = new DateTime(2026, 11, 1),
        Pagina = 1,
        TamanoPagina = 100
    };

    var resultado = await servicio.ListarAsync(filtro);

    var idsObtenidos = resultado.Items
        .Select(x => x.Id)
        .ToHashSet();

    // Debe incluir los tres mensajes del día uruguayo.
    Assert.Contains(mensajes[1].Id, idsObtenidos);
    Assert.Contains(mensajes[2].Id, idsObtenidos);
    Assert.Contains(mensajes[3].Id, idsObtenidos);

    // No debe incluir los mensajes de los días vecinos.
    Assert.DoesNotContain(mensajes[0].Id, idsObtenidos);
    Assert.DoesNotContain(mensajes[4].Id, idsObtenidos);
}

}
