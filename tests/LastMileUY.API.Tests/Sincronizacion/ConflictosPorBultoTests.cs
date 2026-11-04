namespace LastMileUY.API.Tests.Sincronizacion;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Conflictos a nivel de bulto, contra la base real: lo que el celular dice de cada bulto se compara
// con lo que tiene el servidor (docs/sincronizacion.md, "Política de conflictos"). Manda el servidor:
// - un bulto que ya estaba entregado y el celular también entregó no es un conflicto: se saltea;
// - un bulto extraviado, uno que el celular da por no entregado y ya está entregado, o uno que falta
//   entregar y el celular no informó, son conflictos: no se aplica nada de la operación.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ConflictosPorBultoTests(BaseConDosOperadores baseDeDatos)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task Un_bulto_extraviado_en_el_servidor_es_un_conflicto_y_no_se_aplica_nada()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 3);
        var bultos = await BultosAsync(envios[0]);
        var operacion = await OperacionAsync(ruta, envios[0], "EntregaParcial", entregados: [bultos[0], bultos[1]], noEntregados: [bultos[2]]);

        // Mientras el celular estaba sin conexión, en el servidor un bulto quedó extraviado
        await CambiarBultoAsync(bultos[1], EstadoBulto.Extraviado);
        var antes = await FotoDelEnvioAsync(envios[0]);

        var resultado = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Conflicto", resultado.Resultado);
        Assert.Contains("Extraviado", resultado.Mensaje);
        Assert.Contains(await CodigoAsync(bultos[1]), resultado.Mensaje);
        Assert.Equal(antes, await FotoDelEnvioAsync(envios[0]));
    }

    [Fact]
    public async Task Un_bulto_que_el_celular_da_por_no_entregado_y_ya_esta_entregado_es_un_conflicto()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 3);
        var bultos = await BultosAsync(envios[0]);
        var operacion = await OperacionAsync(ruta, envios[0], "EntregaParcial", entregados: [bultos[0]], noEntregados: [bultos[1], bultos[2]]);

        await CambiarBultoAsync(bultos[1], EstadoBulto.Entregado);
        var antes = await FotoDelEnvioAsync(envios[0]);

        var resultado = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Conflicto", resultado.Resultado);
        Assert.Contains("ya está entregado", resultado.Mensaje);
        Assert.Equal(antes, await FotoDelEnvioAsync(envios[0]));
    }

    [Fact]
    public async Task Un_bulto_que_ya_estaba_entregado_y_el_celular_tambien_entrego_se_saltea_y_se_aplica_el_resto()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 3);
        var bultos = await BultosAsync(envios[0]);
        var operacion = await OperacionAsync(ruta, envios[0], "EntregaParcial", entregados: [bultos[0], bultos[1]], noEntregados: [bultos[2]]);

        await CambiarBultoAsync(bultos[0], EstadoBulto.Entregado);

        var resultado = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Aplicada", resultado.Resultado);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var intento = await db.IntentosEntrega.Include(i => i.ResultadosBultos).SingleAsync(i => i.EnvioId == envios[0]);
        // Solo quedan los bultos que faltaban: el que ya estaba entregado no se vuelve a registrar
        Assert.Equal([bultos[1], bultos[2]], intento.ResultadosBultos.Select(r => r.BultoId).Order());
        Assert.Equal("Entregado,Entregado,NoEntregado", (await FotoDelEnvioAsync(envios[0])).Bultos);
    }

    [Fact]
    public async Task Una_entrega_que_no_informa_un_bulto_que_falta_entregar_es_un_conflicto()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 3);
        var bultos = await BultosAsync(envios[0]);

        // El celular conocía dos bultos: el tercero no estaba en su hoja y no se puede dar por entregado
        var operacion = await OperacionAsync(ruta, envios[0], "Entrega", entregados: [bultos[0], bultos[1]], noEntregados: []);
        var antes = await FotoDelEnvioAsync(envios[0]);

        var resultado = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Conflicto", resultado.Resultado);
        Assert.Contains(await CodigoAsync(bultos[2]), resultado.Mensaje);
        Assert.Equal(antes, await FotoDelEnvioAsync(envios[0]));
    }

    [Fact]
    public async Task Una_entrega_con_todos_sus_bultos_queda_entregada()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var bultos = await BultosAsync(envios[0]);
        var operacion = await OperacionAsync(ruta, envios[0], "Entrega", entregados: bultos, noEntregados: []);

        var resultado = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Aplicada", resultado.Resultado);
        Assert.Equal("Entregado", resultado.EstadoEnvio);
        Assert.Equal("Entregado,Entregado", (await FotoDelEnvioAsync(envios[0])).Bultos);
    }

    [Fact]
    public async Task Una_entrega_no_puede_traer_bultos_no_entregados()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: 2);
        var bultos = await BultosAsync(envios[0]);
        var operacion = await OperacionAsync(ruta, envios[0], "Entrega", entregados: [bultos[0]], noEntregados: [bultos[1]]);

        var resultado = await SincronizarAsync(repartidor, operacion);

        Assert.Equal("Rechazada", resultado.Resultado);
        Assert.Equal("EnTransito", (await FotoDelEnvioAsync(envios[0])).Estado);
    }

    // Lo que tiene que quedar igual si la operación no se aplica
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

    private async Task<List<int>> BultosAsync(int envioId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        return await db.Bultos.Where(b => b.EnvioId == envioId).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();
    }

    private async Task<string> CodigoAsync(int bultoId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        return await db.Bultos.Where(b => b.Id == bultoId).Select(b => b.Codigo).SingleAsync();
    }

    // Lo que pasó en el servidor mientras el celular estaba sin conexión
    private async Task CambiarBultoAsync(int bultoId, EstadoBulto estado)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var bulto = await db.Bultos.SingleAsync(b => b.Id == bultoId);
        bulto.CambiarEstado(estado);
        await db.SaveChangesAsync();
    }

    private async Task<ResultadoOperacionDto> SincronizarAsync(string usuarioRepartidor, OperacionRequest operacion)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var servicio = new SincronizarEntregasService(
            db, new ContextoOperadorFijo(OperadorA), new SinAlmacen(), NullLogger<SincronizarEntregasService>.Instance);

        var respuesta = await servicio.EjecutarAsync(new SincronizarRequest { Operaciones = [operacion] }, usuarioRepartidor);
        return Assert.Single(respuesta.Resultados);
    }

    // El operador A pide firma para entregar
    private async Task<OperacionRequest> OperacionAsync(
        int rutaId, int envioId, string tipo, IReadOnlyList<int> entregados, IReadOnlyList<int> noEntregados)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var motivo = await MotivoAsync(db, "Bulto dañado");

        return new OperacionRequest
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
            ResultadosBultos = entregados.Select(id => new ResultadoBultoRequest(id, Entregado: true))
                .Concat(noEntregados.Select(id => new ResultadoBultoRequest(id, Entregado: false, MotivoNoEntregaId: motivo)))
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
