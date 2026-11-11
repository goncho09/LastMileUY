using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Rutas.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Operadores;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LastMileUY.API.Tests.Sincronizacion;

using static BaseConDosOperadores;

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
            // La regla del operador A permite un solo intento: lo pendiente no puede volver a salir
            Assert.Equal("EnDevolucion", resultado.EstadoEnvio);
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

    // --- Política para los bultos pendientes y manifiesto de la hoja de ruta ---

    // El ciclo completo: entrega parcial, el envío se reprograma, sale en otra ruta solo con lo que faltaba
    // y ahí se termina de entregar. El operador B permite dos intentos.
    [Fact]
    public async Task Con_nueva_hoja_de_ruta_lo_pendiente_sale_en_otra_ruta_con_solo_esos_bultos()
    {
        await PublicarAsync(OperadorB, permite: true, destino: "NuevaHojaDeRuta");
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(
            baseDeDatos, OperadorB, bultosPorEnvio: 2);

        var parcial = await SincronizarAsync(repartidor, await EntregaParcialAsync(ruta, envios[0], OperadorB), OperadorB);

        Assert.Equal("Aplicada", parcial.Resultado);
        Assert.Equal("Reprogramado", parcial.EstadoEnvio);
        Assert.Contains("vuelven a salir", parcial.Mensaje);

        // La ruta anterior debe finalizar para liberar el envío.
        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var estadoRutaAnterior = await db.Rutas
                .Where(r => r.Id == ruta)
                .Select(r => r.Estado)
                .SingleAsync();

            Assert.Equal(EstadoRuta.Finalizada, estadoRutaAnterior);
        }

        // La ruta nueva lleva solo el bulto que no se entregó
        int rutaNueva;
        int pendiente;

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            pendiente = await db.Bultos
                .Where(b => b.EnvioId == envios[0] && b.Estado == EstadoBulto.NoEntregado)
                .Select(b => b.Id)
                .SingleAsync();

            var (repartidorNuevo, vehiculoNuevo, _) = await DatosDeRutas.CrearRepartidorYVehiculoAsync(baseDeDatos, OperadorB);
            rutaNueva = await new PlanificarRutaService(db).CrearAsync(new CrearRutaRequest
            {
                Nombre = "Lo que faltaba",
                Fecha = DatosDeRutas.Manana,
                RepartidorId = repartidorNuevo,
                VehiculoId = vehiculoNuevo,
                EnvioIds = [envios[0]]
            }, "usuario-1");
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var manifiesto = await db.BultosParada
                .Where(b => db.Paradas.Any(p => p.Id == b.ParadaId && p.RutaId == rutaNueva))
                .Select(b => b.BultoId)
                .ToListAsync();
            Assert.Equal([pendiente], manifiesto);

            // La hoja muestra lo que lleva la parada, no todo el envío (cada bulto pesa 2 kg)
            var hoja = await new ConsultasRutasService(db).ObtenerDetalleAsync(rutaNueva);
            var parada = Assert.Single(hoja!.Paradas);
            Assert.Equal(1, parada.CantidadBultos);
            Assert.Equal(2, parada.PesoKg);
            Assert.Equal([pendiente], parada.Bultos!.Select(b => b.Id));

            // La ruta vieja sigue mostrando los dos bultos con los que salió
            var hojaVieja = await new ConsultasRutasService(db).ObtenerDetalleAsync(ruta);
            Assert.Equal(2, Assert.Single(hojaVieja!.Paradas).CantidadBultos);
        }

        // Sale la ruta nueva y se entrega lo que faltaba: el envío queda entregado completo
        string repartidorDeLaNueva;

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            await new PlanificarRutaService(db).DespacharAsync(rutaNueva, "usuario-1");
            repartidorDeLaNueva = await db.Rutas.Where(r => r.Id == rutaNueva)
                .Join(db.Repartidores, r => r.RepartidorId, rep => rep.Id, (_, rep) => rep.UsuarioId)
                .SingleAsync();
        }

        var entrega = await EntregaParcialAsync(rutaNueva, envios[0], OperadorB);
        entrega.Tipo = "Entrega";
        entrega.ResultadosBultos = null;

        var final = await SincronizarAsync(repartidorDeLaNueva, entrega, OperadorB);

        Assert.Equal("Aplicada", final.Resultado);
        Assert.Equal("Entregado", final.EstadoEnvio);

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            Assert.All(await db.Bultos.Where(b => b.EnvioId == envios[0]).ToListAsync(),
                b => Assert.Equal(EstadoBulto.Entregado, b.Estado));
        }

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var estadoRutaNueva = await db.Rutas
                .Where(r => r.Id == rutaNueva)
                .Select(r => r.Estado)
                .SingleAsync();

            Assert.Equal(EstadoRuta.Finalizada, estadoRutaNueva);
        }
    }

    [Fact]
    public async Task Recargo_respeta_version_con_la_que_se_despacho_la_ruta()
    {
        // Primera versión: recargo de $150.
        var reglaOriginal = await PublicarAsync(
            OperadorB,
            permite: true,
            destino: "NuevaHojaDeRuta",
            recargo: 150m);

        // La ruta queda asociada a esa versión.
        var (ruta, repartidor, envios) =
            await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos,
                OperadorB,
                bultosPorEnvio: 2);

        decimal costoInicial;

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            costoInicial = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();
        }

        // El administrador publica una nueva versión con $250.
        await PublicarAsync(
            OperadorB,
            permite: true,
            destino: "NuevaHojaDeRuta",
            recargo: 250m);

        // El repartidor sincroniza una entrega parcial.
        var operacion = await EntregaParcialAsync(
            ruta, envios[0], OperadorB);

        var resultado = await SincronizarAsync(
            repartidor, operacion, OperadorB);

        Assert.Equal("Aplicada", resultado.Resultado);
        Assert.Equal("Reprogramado", resultado.EstadoEnvio);

        // Debe utilizar la versión original, no la nueva.
        Assert.Equal(
            reglaOriginal.Id,
            await VersionAplicadaAsync(resultado.IdOperacion, OperadorB));

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var costoFinal = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();

            Assert.Equal(costoInicial + 150m, costoFinal);
        }
    }

    [Fact]
    public async Task Con_devolucion_lo_pendiente_vuelve_al_comercio()
    {
        try
        {
            await PublicarAsync(OperadorA, permite: true, destino: "Devolucion");
            var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos, OperadorA, bultosPorEnvio: 2);

            var resultado = await SincronizarAsync(repartidor, await EntregaParcialAsync(ruta, envios[0]));

            Assert.Equal("EnDevolucion", resultado.EstadoEnvio);
            Assert.Contains("vuelven al comercio", resultado.Mensaje);
        }
        finally
        {
            await PublicarAsync(OperadorA, permite: true);
        }
    }

    // Una ruta que salió antes de que el operador publicara reglas: lo decide el despachador
    [Fact]
    public async Task Si_la_ruta_salio_sin_version_el_envio_queda_entregado_parcialmente()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(
            baseDeDatos, OperadorA, bultosPorEnvio: 2);
        await SinVersionAsync(ruta);

        var resultado = await SincronizarAsync(repartidor, await EntregaParcialAsync(ruta, envios[0]));

        Assert.Equal("Aplicada", resultado.Resultado);
        Assert.Equal("EntregadoParcialmente", resultado.EstadoEnvio);
        Assert.Null(await VersionAplicadaAsync(resultado.IdOperacion));
    }

    // La base no deja que una parada lleve un bulto de otro envío
    [Fact]
    public async Task Una_parada_no_puede_llevar_un_bulto_de_otro_envio()
    {
        var (ruta, _, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, cantidadEnvios: 2);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var paradaDelPrimero = await db.Paradas.Where(p => p.RutaId == ruta && p.EnvioId == envios[0]).Select(p => p.Id).SingleAsync();
        var bultoDelSegundo = await db.Bultos.Where(b => b.EnvioId == envios[1]).Select(b => b.Id).FirstAsync();

        db.BultosParada.Add(new BultoParada { ParadaId = paradaDelPrimero, BultoId = bultoDelSegundo });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [Fact]
    public async Task Nueva_visita_aplica_recargo_una_sola_vez()
    {
        await PublicarAsync(
            OperadorB,
            permite: true,
            destino: "NuevaHojaDeRuta",
            recargo: 150m);

        var (ruta, repartidor, envios) =
            await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos,
                OperadorB,
                bultosPorEnvio: 2);

        decimal costoInicial;

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            costoInicial = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();
        }

        var operacion = await EntregaParcialAsync(
            ruta, envios[0], OperadorB);

        var primera = await SincronizarAsync(
            repartidor, operacion, OperadorB);

        Assert.Equal("Aplicada", primera.Resultado);
        Assert.Equal("Reprogramado", primera.EstadoEnvio);

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var costoFinal = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();

            Assert.Equal(costoInicial + 150m, costoFinal);
        }

        // Reenviamos exactamente la misma operación offline.
        await SincronizarAsync(repartidor, operacion, OperadorB);

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var costoFinal = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();

            Assert.Equal(costoInicial + 150m, costoFinal);
        }
    }

    [Fact]
    public async Task Devolucion_no_aplica_recargo()
    {
        try
        {
            await PublicarAsync(
                OperadorA,
                permite: true,
                destino: "Devolucion",
                recargo: 0m);

            var (ruta, repartidor, envios) =
                await DatosDeRutas.CrearRutaDespachadaAsync(
                    baseDeDatos,
                    OperadorA,
                    bultosPorEnvio: 2);

            decimal costoInicial;

            await using (var db = baseDeDatos.CrearContexto(OperadorA))
            {
                costoInicial = await db.Envios
                    .Where(e => e.Id == envios[0])
                    .Select(e => e.CostoEnvio ?? 0m)
                    .SingleAsync();
            }

            var operacion = await EntregaParcialAsync(
                ruta, envios[0], OperadorA);

            var resultado = await SincronizarAsync(
                repartidor, operacion, OperadorA);

            Assert.Equal("Aplicada", resultado.Resultado);
            Assert.Equal("EnDevolucion", resultado.EstadoEnvio);

            await using (var db = baseDeDatos.CrearContexto(OperadorA))
            {
                var costoFinal = await db.Envios
                    .Where(e => e.Id == envios[0])
                    .Select(e => e.CostoEnvio ?? 0m)
                    .SingleAsync();

                Assert.Equal(costoInicial, costoFinal);
            }
        }
        finally
        {
            await PublicarAsync(OperadorA, permite: true);
        }
    }

    [Fact]
    public async Task Nueva_visita_sin_recargo_mantiene_costo_original()
    {
        await PublicarAsync(
            OperadorB,
            permite: true,
            destino: "NuevaHojaDeRuta",
            recargo: 0m);

        var (ruta, repartidor, envios) =
            await DatosDeRutas.CrearRutaDespachadaAsync(
                baseDeDatos,
                OperadorB,
                bultosPorEnvio: 2);

        decimal costoInicial;

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            costoInicial = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();
        }

        var operacion = await EntregaParcialAsync(
            ruta, envios[0], OperadorB);

        var resultado = await SincronizarAsync(
            repartidor, operacion, OperadorB);

        Assert.Equal("Aplicada", resultado.Resultado);
        Assert.Equal("Reprogramado", resultado.EstadoEnvio);

        await using (var db = baseDeDatos.CrearContexto(OperadorB))
        {
            var costoFinal = await db.Envios
                .Where(e => e.Id == envios[0])
                .Select(e => e.CostoEnvio ?? 0m)
                .SingleAsync();

            Assert.Equal(costoInicial, costoFinal);
        }
    }

    // Como si la ruta se hubiera despachado antes de que existieran las versiones
    private async Task SinVersionAsync(int rutaId)
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand(
            """UPDATE "Rutas" SET "ReglaEntregaParcialId" = NULL WHERE "Id" = @id""", conexion);
        comando.Parameters.AddWithValue("id", rutaId);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<ReglaEntregaParcialDto> PublicarAsync(
        int operadorId,
        bool permite,
        string destino = "NuevaHojaDeRuta",
        decimal recargo = 0m)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);

        return await Servicio(db, operadorId).PublicarAsync(
            new PublicarReglaEntregaParcialRequest
            {
                PermiteEntregaParcial = permite,
                DestinoBultosPendientes = destino,
                RecargoNuevaVisita = recargo,
                Motivo = permite ? "Se permiten entregas parciales" : "No se permiten entregas parciales"
            },
            "usuario-1");
    }

    private static ReglasEntregaParcialService Servicio(
        ApplicationDbContext db, int operadorId) =>
        new(db, new ContextoOperadorFijo(operadorId));

    private async Task<ResultadoOperacionDto> SincronizarAsync(
        string usuarioRepartidor, OperacionRequest operacion, int operadorId = OperadorA)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        var servicio = new SincronizarEntregasService(
            db, new ContextoOperadorFijo(operadorId), new SinAlmacen(), NullLogger<SincronizarEntregasService>.Instance);

        var respuesta = await servicio.EjecutarAsync(new SincronizarRequest { Operaciones = [operacion] }, usuarioRepartidor);
        return Assert.Single(respuesta.Resultados);
    }

    // Entrega el primer bulto y deja el otro sin entregar, con toda la evidencia que pueden pedir
    // las reglas de prueba (A pide firma; B, foto y documento)
    private async Task<OperacionRequest> EntregaParcialAsync(int rutaId, int envioId, int operadorId = OperadorA)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
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
            DocumentoReceptor = "1.234.567-8",
            FirmaBase64 = Png,
            FotoBase64 = Png,
            ResultadosBultos =
            [
                new ResultadoBultoRequest(bultos[0], Entregado: true),
                new ResultadoBultoRequest(bultos[1], Entregado: false, MotivoNoEntregaId: motivo)
            ]
        };
    }

    private static async Task<int> MotivoAsync(ApplicationDbContext db)
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

    private async Task<int?> VersionAplicadaAsync(
        Guid idOperacion,
        int operadorId = OperadorA)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);

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
