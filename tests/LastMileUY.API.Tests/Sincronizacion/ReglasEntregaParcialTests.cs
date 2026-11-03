namespace LastMileUY.API.Tests.Sincronizacion;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Reglas de entrega parcial versionadas, contra la base real con RLS:
// - las versiones se numeran por operador y no se pueden modificar ni borrar (lo frena la base);
// - una entrega se evalúa con la versión con la que salió su hoja de ruta, no con la vigente al sincronizar.
// La base es compartida: las pruebas que publican una versión que no permite parciales
// dejan publicada al final otra que sí, para no cambiarles el comportamiento a las demás.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class ReglasEntregaParcialTests(BaseConDosOperadores baseDeDatos)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Fact]
    public async Task Las_versiones_se_numeran_por_operador_y_cada_uno_ve_solo_las_suyas()
    {
        var primera = await PublicarAsync(OperadorA, permite: true);
        var segunda = await PublicarAsync(OperadorA, permite: false, destino: "Devolucion");
        await PublicarAsync(OperadorA, permite: true);

        Assert.Equal(primera.Version + 1, segunda.Version);
        Assert.False(segunda.PermiteEntregaParcial);
        Assert.Equal("Devolucion", segunda.DestinoBultosPendientes);

        await using var dbA = baseDeDatos.CrearContexto(OperadorA);
        var deA = await Servicio(dbA, OperadorA).ListarAsync();
        Assert.Equal(deA.Select(r => r.Version).OrderDescending(), deA.Select(r => r.Version));
        Assert.Contains(deA, r => r.Id == segunda.Id);

        await using var dbB = baseDeDatos.CrearContexto(OperadorB);
        var deB = await Servicio(dbB, OperadorB).ListarAsync();
        Assert.DoesNotContain(deB, r => r.Id == primera.Id || r.Id == segunda.Id);
    }

    // Es lo que hace confiable el historial: ni un error en el código puede reescribir
    // las reglas con las que ya se evaluaron entregas
    [Theory]
    [InlineData("""UPDATE "ReglasEntregaParcial" SET "PermiteEntregaParcial" = NOT "PermiteEntregaParcial" """)]
    [InlineData("""DELETE FROM "ReglasEntregaParcial" """)]
    public async Task Una_version_publicada_no_se_puede_modificar_ni_borrar(string sql)
    {
        await PublicarAsync(OperadorA, permite: true);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Theory]
    [InlineData("Tirarlos", "Cambio de política")]
    [InlineData("Devolucion", "   ")]
    public async Task Sin_un_destino_valido_o_sin_motivo_no_se_publica(string destino, string motivo)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(() => Servicio(db, OperadorA).PublicarAsync(
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = true,
                DestinoBultosPendientes = destino,
                Motivo = motivo
            },
            "usuario-1"));
    }

    [Fact]
    public async Task Vale_la_version_con_la_que_salio_la_ruta_aunque_despues_se_publique_una_que_permite()
    {
        try
        {
            var sinParciales = await PublicarAsync(OperadorA, permite: false);
            var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos, OperadorA, bultosPorEnvio: 2);

            // Mientras el repartidor está en la calle, el operador vuelve a permitirlas
            await PublicarAsync(OperadorA, permite: true);

            var resultado = await SincronizarAsync(repartidor, await EntregaParcialAsync(ruta, envios[0]));

            Assert.Equal("Rechazada", resultado.Resultado);
            Assert.Contains($"versión {sinParciales.Version}", resultado.Mensaje);
            Assert.Equal("EnTransito", resultado.EstadoEnvio);
            Assert.Equal(sinParciales.Id, await VersionAplicadaAsync(resultado.IdOperacion));
        }
        finally
        {
            await PublicarAsync(OperadorA, permite: true);
        }
    }

    [Fact]
    public async Task Vale_la_version_con_la_que_salio_la_ruta_aunque_despues_se_publique_una_que_no_permite()
    {
        try
        {
            var conParciales = await PublicarAsync(OperadorA, permite: true);
            var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos, OperadorA, bultosPorEnvio: 2);

            await PublicarAsync(OperadorA, permite: false);

            var resultado = await SincronizarAsync(repartidor, await EntregaParcialAsync(ruta, envios[0]));

            Assert.Equal("Aplicada", resultado.Resultado);
            Assert.Equal("EntregadoParcialmente", resultado.EstadoEnvio);
            Assert.Equal(conParciales.Id, await VersionAplicadaAsync(resultado.IdOperacion));

            // Una ruta nueva ya sale con la versión que no las permite
            var (rutaNueva, repartidorNuevo, enviosNuevos) = await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos, OperadorA, bultosPorEnvio: 2);
            var enLaNueva = await SincronizarAsync(repartidorNuevo, await EntregaParcialAsync(rutaNueva, enviosNuevos[0]));

            Assert.Equal("Rechazada", enLaNueva.Resultado);
        }
        finally
        {
            await PublicarAsync(OperadorA, permite: true);
        }
    }

    private async Task<ReglaEntregaParcialDto> PublicarAsync(
        int operadorId, bool permite, string destino = "NuevaHojaDeRuta")
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);

        return await Servicio(db, operadorId).PublicarAsync(
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = permite,
                DestinoBultosPendientes = destino,
                Motivo = permite ? "Se permiten entregas parciales" : "No se permiten entregas parciales"
            },
            "usuario-1");
    }

    private static ReglasEntregaParcialService Servicio(
        API.Infrastructure.Persistence.ApplicationDbContext db, int operadorId) =>
        new(db, new ContextoOperadorFijo(operadorId));

    private async Task<ResultadoOperacionDto> SincronizarAsync(string usuarioRepartidor, OperacionRequest operacion)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var servicio = new SincronizarEntregasService(
            db, new ContextoOperadorFijo(OperadorA), new SinAlmacen(), NullLogger<SincronizarEntregasService>.Instance);

        var respuesta = await servicio.EjecutarAsync(new SincronizarRequest { Operaciones = [operacion] }, usuarioRepartidor);
        return Assert.Single(respuesta.Resultados);
    }

    // Entrega el primer bulto y deja el otro sin entregar, con la firma que pide la regla del operador A
    private async Task<OperacionRequest> EntregaParcialAsync(int rutaId, int envioId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var bultos = await db.Bultos.Where(b => b.EnvioId == envioId).OrderBy(b => b.Id).Select(b => b.Id).ToListAsync();
        var motivo = await MotivoAsync(db);

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
            ResultadosBultos =
            [
                new ResultadoBultoRequest(bultos[0], Entregado: true),
                new ResultadoBultoRequest(bultos[1], Entregado: false, MotivoNoEntregaId: motivo)
            ]
        };
    }

    private static async Task<int> MotivoAsync(API.Infrastructure.Persistence.ApplicationDbContext db)
    {
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Activo);

        if (motivo is null)
        {
            motivo = new MotivoNoEntrega { Nombre = "Bulto dañado" };
            db.MotivosNoEntrega.Add(motivo);
            await db.SaveChangesAsync();
        }

        return motivo.Id;
    }

    private async Task<int?> VersionAplicadaAsync(Guid idOperacion)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        return await db.OperacionesSincronizadas
            .Where(o => o.IdOperacion == idOperacion)
            .Select(o => o.ReglaEntregaParcialId)
            .SingleAsync();
    }

    // Las imágenes no importan acá: se descartan
    private class SinAlmacen : IAlmacenEvidencias
    {
        public Task<string> GuardarAsync(int operadorId, Imagen imagen, CancellationToken ct = default) =>
            Task.FromResult($"{operadorId}/{Guid.NewGuid():N}.{imagen.Extension}");

        public Task<Imagen?> LeerAsync(string clave, CancellationToken ct = default) =>
            Task.FromResult<Imagen?>(null);
    }
}
