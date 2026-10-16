namespace LastMileUY.API.Tests.Sincronizacion;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Sincronizacion.Application;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Sincronización de la app del repartidor contra la base real: evidencia según la regla de cada
// operador, reenvíos sin duplicar, conflictos, resultado por operación y avance de la ruta.
// Reglas de la base de prueba: el operador A pide firma; el B pide foto y documento del receptor.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class SincronizacionTests(BaseConDosOperadores baseDeDatos)
{
    // Lo mínimo para que se reconozca como PNG (alcanza para la validación)
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    private readonly AlmacenEnMemoria almacen = new();

    [Fact]
    public async Task Una_entrega_con_la_evidencia_que_pide_la_regla_se_aplica()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);
        var hace2Horas = DateTime.UtcNow.AddHours(-2);

        var resultado = Assert.Single((await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, envios[0], firma: Png, fecha: hace2Horas))).Resultados);

        Assert.Equal("Aplicada", resultado.Resultado);
        Assert.Equal("Entregado", resultado.EstadoEnvio);
        Assert.False(resultado.Duplicada);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var envio = await db.Envios
            .Include(e => e.IntentosEntrega).ThenInclude(i => i.PruebaEntrega)
            .FirstAsync(e => e.Id == envios[0]);

        // Se usa la hora del celular, no la de llegada
        Assert.Equal(hace2Horas, envio.FechaEntrega!.Value, TimeSpan.FromSeconds(1));
        var intento = Assert.Single(envio.IntentosEntrega);
        Assert.True(intento.Exitoso);
        Assert.NotNull(intento.ParadaId);
        Assert.Equal("Ana Silva", intento.PruebaEntrega!.NombreReceptor);
        Assert.NotNull(await almacen.LeerAsync(intento.PruebaEntrega.FirmaArchivoKey!));

        // El evento queda en el historial con la ubicación
        var evento = await db.EventosEnvio.Where(e => e.EnvioId == envios[0]).OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal("Repartidor", evento.Origen);
        Assert.Equal(-34.9m, evento.Latitud);

        // La única parada se resolvió: la ruta arrancó y terminó
        var rutaGuardada = await db.Rutas.FirstAsync(r => r.Id == ruta);
        Assert.Equal(EstadoRuta.Finalizada, rutaGuardada.Estado);
        Assert.Equal(hace2Horas, rutaGuardada.FechaInicio!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Sin_la_firma_que_pide_la_regla_del_operador_A_se_rechaza()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);

        var resultado = Assert.Single((await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, envios[0], firma: null))).Resultados);

        Assert.Equal("Rechazada", resultado.Resultado);
        Assert.Contains("la firma", resultado.Mensaje);
        Assert.Equal("EnTransito", resultado.EstadoEnvio);
    }

    [Fact]
    public async Task El_operador_B_pide_foto_y_documento_en_lugar_de_firma()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorB, 2);

        var resultados = (await SincronizarAsync(OperadorB, repartidor,
            // Con firma pero sin foto ni documento: no alcanza para el operador B
            Entrega(ruta, envios[0], firma: Png),
            Entrega(ruta, envios[1], firma: null, foto: Png, documento: "1.234.567-8"))).Resultados;

        Assert.Equal("Rechazada", resultados[0].Resultado);
        Assert.Contains("una foto y el nombre y el documento", resultados[0].Mensaje);
        Assert.Equal("Aplicada", resultados[1].Resultado);
    }

    [Fact]
    public async Task Reenviar_la_misma_operacion_no_la_aplica_dos_veces()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);
        var operacion = Entrega(ruta, envios[0], firma: Png);

        var primera = Assert.Single((await SincronizarAsync(OperadorA, repartidor, operacion)).Resultados);
        // La app no recibió la respuesta y la manda otra vez
        var segunda = Assert.Single((await SincronizarAsync(OperadorA, repartidor, operacion)).Resultados);

        Assert.Equal("Aplicada", primera.Resultado);
        Assert.Equal("Aplicada", segunda.Resultado);
        Assert.True(segunda.Duplicada);

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        Assert.Equal(1, await db.IntentosEntrega.CountAsync(i => i.EnvioId == envios[0]));
        Assert.Equal(1, await db.OperacionesSincronizadas.CountAsync(o => o.IdOperacion == operacion.IdOperacion));
    }

    [Fact]
    public async Task La_misma_operacion_dos_veces_en_un_lote_se_aplica_una_sola_vez()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);
        var operacion = Entrega(ruta, envios[0], firma: Png);

        var resultados = (await SincronizarAsync(OperadorA, repartidor, operacion, operacion)).Resultados;

        Assert.False(resultados[0].Duplicada);
        Assert.True(resultados[1].Duplicada);
    }

    [Fact]
    public async Task Si_el_backoffice_cambio_el_envio_mientras_tanto_es_un_conflicto_y_manda_el_servidor()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);

        // Mientras el repartidor estaba sin señal, el despachador devolvió el envío al depósito
        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var envio = await db.Envios.FirstAsync(e => e.Id == envios[0]);
            envio.CambiarEstado(EstadoEnvio.EnDeposito, "Backoffice");
            await db.SaveChangesAsync();
        }

        var resultado = Assert.Single((await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, envios[0], firma: Png))).Resultados);

        Assert.Equal("Conflicto", resultado.Resultado);
        Assert.Contains("EnDeposito", resultado.Mensaje);
        Assert.Equal("EnDeposito", resultado.EstadoEnvio);
        Assert.False(resultado.Reintentar);
    }

    [Fact]
    public async Task Dos_operaciones_distintas_sobre_el_mismo_envio_la_segunda_es_conflicto()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, 2);
        // Dos envíos: así la ruta sigue en curso después de la primera operación (si no, termina)

        var resultados = (await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, envios[0], firma: Png),
            IntentoFallido(ruta, envios[0], await MotivoAsync(OperadorA)))).Resultados;

        Assert.Equal("Aplicada", resultados[0].Resultado);
        Assert.Equal("Conflicto", resultados[1].Resultado);
        Assert.Contains("ya está Entregado", resultados[1].Mensaje);
    }

    [Fact]
    public async Task Un_intento_fallido_registra_el_motivo_y_la_ruta_termina_cuando_no_queda_nada_en_transito()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, 2);
        var motivo = await MotivoAsync(OperadorA);

        var primero = Assert.Single((await SincronizarAsync(OperadorA, repartidor,
            IntentoFallido(ruta, envios[0], motivo, "Nadie atendió el timbre"))).Resultados);

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            // Queda un envío en tránsito: la ruta sigue en curso
            Assert.Equal(EstadoRuta.EnCurso, (await db.Rutas.FirstAsync(r => r.Id == ruta)).Estado);

            var intento = await db.IntentosEntrega.SingleAsync(i => i.EnvioId == envios[0]);
            Assert.False(intento.Exitoso);
            Assert.Equal(motivo, intento.MotivoNoEntregaId);
            Assert.Contains("Nadie atendió el timbre", intento.Observaciones);
        }

        Assert.Equal("NoEntregado", primero.EstadoEnvio);

        await SincronizarAsync(OperadorA, repartidor, Entrega(ruta, envios[1], firma: Png));

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            Assert.Equal(EstadoRuta.Finalizada, (await db.Rutas.FirstAsync(r => r.Id == ruta)).Estado);
        }
    }

    [Fact]
    public async Task Un_intento_fallido_sin_motivo_se_rechaza()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);

        var resultado = Assert.Single((await SincronizarAsync(OperadorA, repartidor,
            IntentoFallido(ruta, envios[0], motivoId: null))).Resultados);

        Assert.Equal("Rechazada", resultado.Resultado);
    }

    [Fact]
    public async Task Cada_operacion_tiene_su_resultado_y_una_mala_no_frena_a_las_demas()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, 3);
        var noEsPng = Convert.ToBase64String("esto no es una imagen"u8.ToArray());

        var resultados = (await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, envios[0], firma: noEsPng),
            Entrega(ruta, envios[1], firma: Png, fecha: DateTime.UtcNow.AddHours(1)),
            Entrega(ruta, envios[2], firma: Png))).Resultados;

        Assert.Equal(3, resultados.Count);
        Assert.Contains("PNG o JPEG", resultados[0].Mensaje);
        Assert.Contains("futuro", resultados[1].Mensaje);
        Assert.Equal(["Rechazada", "Rechazada", "Aplicada"], resultados.Select(r => r.Resultado));
    }

    [Fact]
    public async Task No_se_sincroniza_sobre_la_ruta_de_otro_repartidor_ni_de_otro_operador()
    {
        var (ruta, _, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);
        var (_, otroRepartidor, _) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);
        var (_, repartidorDeB, _) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorB);

        var deOtroRepartidor = Assert.Single((await SincronizarAsync(OperadorA, otroRepartidor,
            Entrega(ruta, envios[0], firma: Png))).Resultados);
        var deOtroOperador = Assert.Single((await SincronizarAsync(OperadorB, repartidorDeB,
            Entrega(ruta, envios[0], firma: Png))).Resultados);

        Assert.Contains("otro repartidor", deOtroRepartidor.Mensaje);
        // Para el operador B la ruta del A no existe (EF + RLS), y el envío tampoco
        Assert.Contains("no existe", deOtroOperador.Mensaje);
        Assert.Null(deOtroOperador.EstadoEnvio);
    }

    [Fact]
    public async Task Un_envio_que_no_esta_en_la_ruta_se_rechaza()
    {
        var (ruta, repartidor, _) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);
        var (_, _, otrosEnvios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA);

        var resultado = Assert.Single((await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, otrosEnvios[0], firma: Png))).Resultados);

        Assert.Contains("no está en esta ruta", resultado.Mensaje);
    }

    [Fact]
    public async Task Un_lote_vacio_o_demasiado_grande_es_un_error()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var servicio = Servicio(db, OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servicio.EjecutarAsync(new SincronizarRequest(), "usuario"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servicio.EjecutarAsync(new SincronizarRequest
            {
                Operaciones = Enumerable.Range(0, SincronizarEntregasService.MaximoOperaciones + 1)
                    .Select(_ => Entrega(1, 1, Png))
                    .ToList()
            }, "usuario"));
    }

    [Fact]
    public async Task El_backoffice_ve_lo_que_llego_con_su_resultado_y_la_evidencia_pero_no_lo_de_otro_operador()
    {
        var (ruta, repartidor, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, 2);

        await SincronizarAsync(OperadorA, repartidor,
            Entrega(ruta, envios[0], firma: Png),
            Entrega(ruta, envios[1], firma: null));

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var consultas = new ConsultasSincronizacionService(db, almacen);

        var operaciones = (await consultas.ListarOperacionesAsync(1, 20, ruta, null)).Items;
        Assert.Equal(2, operaciones.Count);
        var aplicada = Assert.Single(operaciones, o => o.Resultado == "Aplicada");
        Assert.True(aplicada.TieneFirma);
        Assert.Single(operaciones, o => o.Resultado == "Rechazada");

        Assert.NotNull(await consultas.ObtenerEvidenciaAsync(aplicada.IntentoEntregaId!.Value, "firma"));
        Assert.Null(await consultas.ObtenerEvidenciaAsync(aplicada.IntentoEntregaId.Value, "foto"));

        // El operador B no ve ni las operaciones ni la evidencia
        await using var dbB = baseDeDatos.CrearContexto(OperadorB);
        var consultasB = new ConsultasSincronizacionService(dbB, almacen);
        Assert.Empty((await consultasB.ListarOperacionesAsync(1, 20, ruta, null)).Items);
        Assert.Null(await consultasB.ObtenerEvidenciaAsync(aplicada.IntentoEntregaId.Value, "firma"));
    }

    private async Task<SincronizarResponse> SincronizarAsync(
        int operadorId, string usuarioRepartidor, params OperacionRequest[] operaciones)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);

        return await Servicio(db, operadorId).EjecutarAsync(
            new SincronizarRequest { Operaciones = [.. operaciones] }, usuarioRepartidor);
    }

    private SincronizarEntregasService Servicio(API.Infrastructure.Persistence.ApplicationDbContext db, int operadorId) =>
        new(db, new ContextoOperadorFijo(operadorId), almacen, NullLogger<SincronizarEntregasService>.Instance);

    private static OperacionRequest Entrega(
        int rutaId, int envioId, string? firma, string? foto = null, string? documento = null, DateTime? fecha = null) => new()
    {
        IdOperacion = Guid.NewGuid(),
        Tipo = "Entrega",
        RutaId = rutaId,
        EnvioId = envioId,
        FechaHoraDispositivo = fecha ?? DateTime.UtcNow.AddMinutes(-10),
        Latitud = -34.9m,
        Longitud = -56.16m,
        NombreReceptor = "Ana Silva",
        DocumentoReceptor = documento,
        FirmaBase64 = firma,
        FotoBase64 = foto
    };

    private static OperacionRequest IntentoFallido(int rutaId, int envioId, int? motivoId, string? observaciones = null) => new()
    {
        IdOperacion = Guid.NewGuid(),
        Tipo = "IntentoFallido",
        RutaId = rutaId,
        EnvioId = envioId,
        FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-10),
        Latitud = -34.9m,
        Longitud = -56.16m,
        MotivoNoEntregaId = motivoId,
        Observaciones = observaciones
    };

    // Un motivo de no entrega del operador (lo crea si todavía no tiene)
    private async Task<int> MotivoAsync(int operadorId)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync();

        if (motivo is null)
        {
            motivo = new MotivoNoEntrega { Nombre = "Destinatario ausente" };
            db.MotivosNoEntrega.Add(motivo);
            await db.SaveChangesAsync();
        }

        return motivo.Id;
    }

    // Guarda las imágenes en memoria en vez de en disco
    private class AlmacenEnMemoria : IAlmacenEvidencias
    {
        private readonly Dictionary<string, Imagen> imagenes = [];

        public Task<string> GuardarAsync(int operadorId, Imagen imagen, CancellationToken ct = default)
        {
            var clave = $"{operadorId}/{Guid.NewGuid():N}.{imagen.Extension}";
            imagenes[clave] = imagen;
            return Task.FromResult(clave);
        }

        public Task<Imagen?> LeerAsync(string clave, CancellationToken ct = default) =>
            Task.FromResult(imagenes.GetValueOrDefault(clave));
    }
}
