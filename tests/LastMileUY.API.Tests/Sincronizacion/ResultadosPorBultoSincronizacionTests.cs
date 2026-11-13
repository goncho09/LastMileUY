using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LastMileUY.API.Tests.Sincronizacion;

using static BaseConDosOperadores;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ResultadosPorBultoSincronizacionTests(BaseConDosOperadores baseDeDatos)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task Reenviar_una_entrega_parcial_no_duplica_los_resultados_de_los_bultos()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var operacion = await EntregaParcialAsync(ruta, envios[0]);

        var primera = await SincronizarAsync(repartidor, operacion);
        var despuesDeLaPrimera = await FotoDelEnvioAsync(envios[0]);

        var segunda = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Aplicada", primera.Resultado);
        Assert.False(primera.Duplicada);
        Assert.True(segunda.Duplicada);
        Assert.Equal(primera.Resultado, segunda.Resultado);
        Assert.Equal(primera.Mensaje, segunda.Mensaje);

        var despuesDeLaSegunda = await FotoDelEnvioAsync(envios[0]);
        Assert.Equal(1, despuesDeLaSegunda.Intentos);
        Assert.Equal(2, despuesDeLaSegunda.ResultadosBultos);
        Assert.Equal(despuesDeLaPrimera, despuesDeLaSegunda);
    }

    [Fact]
    public async Task Los_mismos_resultados_en_otro_orden_son_el_mismo_reenvio()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 3);
        var operacion = await EntregaParcialAsync(ruta, envios[0]);

        await SincronizarAsync(repartidor, operacion);

        operacion.ResultadosBultos!.Reverse();
        var reenvio = await SincronizarAsync(repartidor, operacion);

        Assert.True(reenvio.Duplicada);
        Assert.Equal("Aplicada", reenvio.Resultado);
    }

    [Fact]
    public async Task Un_reenvio_con_otro_resultado_para_un_bulto_se_rechaza_sin_tocar_lo_anterior()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var operacion = await EntregaParcialAsync(ruta, envios[0]);

        await SincronizarAsync(repartidor, operacion);
        var antes = await FotoDelEnvioAsync(envios[0]);

        var resultados = operacion.ResultadosBultos!;
        operacion.ResultadosBultos =
        [
            new ResultadoBultoRequest(resultados[0].BultoId, Entregado: false, MotivoNoEntregaId: resultados[1].MotivoNoEntregaId),
            new ResultadoBultoRequest(resultados[1].BultoId, Entregado: true)
        ];

        var otra = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Rechazada", otra.Resultado);
        Assert.False(otra.Duplicada);
        Assert.False(otra.Reintentar);
        Assert.Contains("otro contenido", otra.Mensaje);

        Assert.Equal(antes, await FotoDelEnvioAsync(envios[0]));

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var registrada = await db.OperacionesSincronizadas.SingleAsync(o => o.IdOperacion == operacion.IdOperacion);
        Assert.Equal("Aplicada", registrada.Resultado.ToString());
    }

    [Fact]
    public async Task Un_reenvio_de_un_intento_fallido_con_otro_motivo_se_rechaza()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);

        int motivo;
        int otroMotivo;
        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            motivo = await MotivoAsync(db, "Nadie en el domicilio");
            otroMotivo = await MotivoAsync(db, "Dirección incorrecta");
        }

        var operacion = new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(),
            Tipo = "IntentoFallido",
            RutaId = ruta,
            EnvioId = envios[0],
            FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-10),
            Latitud = -34.9m,
            Longitud = -56.16m,
            MotivoNoEntregaId = motivo
        };

        Assert.Equal("Aplicada", (await SincronizarAsync(repartidor, operacion)).Resultado);

        operacion.MotivoNoEntregaId = otroMotivo;
        var otra = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Rechazada", otra.Resultado);
        Assert.Contains("otro contenido", otra.Mensaje);
    }

    [Fact]
    public async Task Dos_reenvios_a_la_vez_aplican_la_entrega_parcial_una_sola_vez()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var operacion = await EntregaParcialAsync(ruta, envios[0]);

        var resultados = await Task.WhenAll(
            SincronizarAsync(repartidor, operacion),
            SincronizarAsync(repartidor, operacion));

        Assert.Single(resultados, r => r is { Resultado: "Aplicada", Duplicada: false });
        Assert.Single(resultados, r => r.Duplicada || r.Reintentar);

        var reintento = await SincronizarAsync(repartidor, operacion);
        Assert.True(reintento.Duplicada);
        Assert.Equal("Aplicada", reintento.Resultado);

        var foto = await FotoDelEnvioAsync(envios[0]);
        Assert.Equal(1, foto.Intentos);
        Assert.Equal(2, foto.ResultadosBultos);
    }

    private record Foto(int Intentos, int ResultadosBultos, int Eventos, string Estado, string Bultos);

    private async Task<Foto> FotoDelEnvioAsync(int envioId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var envio = await db.Envios.Include(e => e.Bultos).SingleAsync(e => e.Id == envioId);

        return new Foto(
            await db.IntentosEntrega.CountAsync(i => i.EnvioId == envioId),
            await db.ResultadosBultosIntentos.CountAsync(r => r.IntentoEntrega.EnvioId == envioId),
            await db.EventosEnvio.CountAsync(e => e.EnvioId == envioId),
            envio.Estado.ToString(),
            string.Join(",", envio.Bultos.OrderBy(b => b.Id).Select(b => b.Estado)));
    }

    private async Task<ResultadoOperacionDto> SincronizarAsync(string usuarioRepartidor, OperacionRequest operacion)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var servicio = new SincronizarEntregasService(
            db, new ContextoOperadorFijo(OperadorA), new SinAlmacen(), NullLogger<SincronizarEntregasService>.Instance);

        var respuesta = await servicio.EjecutarAsync(new SincronizarRequest { Operaciones = [operacion] }, usuarioRepartidor);
        return Assert.Single(respuesta.Resultados);
    }

    private async Task<OperacionRequest> EntregaParcialAsync(int rutaId, int envioId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var bultos = await db.Bultos.Where(b => b.EnvioId == envioId).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();
        var motivo = await MotivoAsync(db, "Bulto dañado");

        return new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(),
            Tipo = "EntregaParcial",
            RutaId = rutaId,
            EnvioId = envioId,
            FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-10),
            Latitud = -34.9m,
            Longitud = -56.16m,
            NombreReceptor = "Ana Silva",
            FirmaBase64 = Png,
            ResultadosBultos = bultos
                .Select((id, i) => i < bultos.Count - 1
                    ? new ResultadoBultoRequest(id, Entregado: true)
                    : new ResultadoBultoRequest(id, Entregado: false, MotivoNoEntregaId: motivo))
                .ToList()
        };
    }

    private static async Task<int> MotivoAsync(ApplicationDbContext db, string nombre)
    {
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
