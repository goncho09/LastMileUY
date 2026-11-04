namespace LastMileUY.API.Tests.Envios;

using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// El detalle del envío (Backoffice y portal del comercio) muestra qué pasó con cada bulto en cada
// intento, y la prueba de entrega de cada intento respalda solo los bultos que se entregaron en él.
// Contra la base real con RLS, después de sincronizar desde la app del repartidor.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ResultadosBultosDetalleTests(BaseConDosOperadores baseDeDatos)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task Una_entrega_parcial_muestra_el_resultado_de_cada_bulto_y_su_prueba_solo_cubre_los_entregados()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 3);
        var bultos = await BultosAsync(envios[0]);
        var danado = await MotivoAsync("Bulto dañado");

        await SincronizarAsync(repartidor, Operacion(ruta, envios[0], "EntregaParcial",
        [
            new ResultadoBultoRequest(bultos[0].Id, Entregado: true),
            new ResultadoBultoRequest(bultos[1].Id, Entregado: true),
            new ResultadoBultoRequest(bultos[2].Id, Entregado: false, MotivoNoEntregaId: danado)
        ]));

        var detalle = await DetalleAsync(envios[0]);

        var intento = Assert.Single(detalle.Intentos);
        Assert.Equal("Parcial", intento.Resultado);
        Assert.Equal(
            [
                new ResultadoBultoDto(bultos[0].Codigo, true, null),
                new ResultadoBultoDto(bultos[1].Codigo, true, null),
                new ResultadoBultoDto(bultos[2].Codigo, false, "Bulto dañado")
            ],
            intento.Bultos);

        // La firma de quien recibió no respalda el bulto que no se entregó
        var prueba = Assert.IsType<PruebaEntregaDto>(intento.Prueba);
        Assert.Equal([bultos[0].Codigo, bultos[1].Codigo], prueba.BultosEntregados);
        Assert.True(prueba.TieneFirma);
        Assert.False(prueba.TieneFoto);
        Assert.Equal("Ana Silva", prueba.NombreReceptor);

        // Y cada bulto con su estado
        Assert.Equal(["Entregado", "Entregado", "NoEntregado"], detalle.Bultos.Select(b => b.Estado));
    }

    [Fact]
    public async Task Un_intento_fallido_muestra_el_motivo_de_cada_bulto_y_no_tiene_prueba()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var nadie = await MotivoAsync("No había nadie");

        var operacion = Operacion(ruta, envios[0], "IntentoFallido", null);
        operacion.MotivoNoEntregaId = nadie;
        await SincronizarAsync(repartidor, operacion);

        var intento = Assert.Single((await DetalleAsync(envios[0])).Intentos);

        Assert.Equal("NoEntregado", intento.Resultado);
        Assert.Equal("No había nadie", intento.Motivo);
        Assert.All(intento.Bultos, b => Assert.Equal((false, "No había nadie"), (b.Entregado, b.Motivo)));
        Assert.Null(intento.Prueba);
    }

    [Fact]
    public async Task El_comercio_ve_el_resultado_de_los_bultos_de_su_envio()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var bultos = await BultosAsync(envios[0]);

        await SincronizarAsync(repartidor, Operacion(ruta, envios[0], "Entrega",
            bultos.Select(b => new ResultadoBultoRequest(b.Id, Entregado: true)).ToList()));

        var detalle = await DetalleAsync(envios[0], comercioId: ComercioCompartido);

        var intento = Assert.Single(detalle.Intentos);
        Assert.Equal("Entregado", intento.Resultado);
        Assert.Equal(bultos.Select(b => b.Codigo), intento.Prueba!.BultosEntregados);
        Assert.All(detalle.Bultos, b => Assert.Equal("Entregado", b.Estado));
    }

    // Como lo pide el Backoffice (personal del operador) o el portal (usuario del comercio)
    private async Task<EnvioDetalleDto> DetalleAsync(int envioId, int? comercioId = null)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, comercioId);
        var usuario = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, comercioId is null ? "Operador" : "Comercio")], "Test"))
        };

        var detalle = await new ConsultasEnviosService(
                db, new ContextoOperadorFijo(OperadorA, comercioId), new HttpContextAccessor { HttpContext = usuario })
            .ObtenerDetalleAsync(envioId);

        return Assert.IsType<EnvioDetalleDto>(detalle);
    }

    private async Task<List<(int Id, string Codigo)>> BultosAsync(int envioId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var bultos = await db.Bultos.Where(b => b.EnvioId == envioId).OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.Codigo }).ToListAsync();

        return bultos.Select(b => (b.Id, b.Codigo)).ToList();
    }

    private async Task SincronizarAsync(string usuarioRepartidor, OperacionRequest operacion)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var servicio = new SincronizarEntregasService(
            db, new ContextoOperadorFijo(OperadorA), new SinAlmacen(), NullLogger<SincronizarEntregasService>.Instance);

        var respuesta = await servicio.EjecutarAsync(new SincronizarRequest { Operaciones = [operacion] }, usuarioRepartidor);
        Assert.Equal("Aplicada", Assert.Single(respuesta.Resultados).Resultado);
    }

    // El operador A pide firma para entregar
    private static OperacionRequest Operacion(int rutaId, int envioId, string tipo, List<ResultadoBultoRequest>? resultados) => new()
    {
        IdOperacion = Guid.NewGuid(),
        Tipo = tipo,
        RutaId = rutaId,
        EnvioId = envioId,
        FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-10),
        Latitud = -34.9m,
        Longitud = -56.16m,
        NombreReceptor = "Ana Silva",
        FirmaBase64 = Png,
        ResultadosBultos = resultados
    };

    private async Task<int> MotivoAsync(string nombre)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Activo && m.Nombre == nombre);

        if (motivo is null)
        {
            motivo = new MotivoNoEntrega { Nombre = nombre };
            db.MotivosNoEntrega.Add(motivo);
            await db.SaveChangesAsync();
        }

        return motivo.Id;
    }

    private class SinAlmacen : IAlmacenEvidencias
    {
        public Task<string> GuardarAsync(int operadorId, Imagen imagen, CancellationToken ct = default) =>
            Task.FromResult($"{operadorId}/{Guid.NewGuid():N}.{imagen.Extension}");

        public Task<Imagen?> LeerAsync(string clave, CancellationToken ct = default) =>
            Task.FromResult<Imagen?>(null);
    }
}
