using System.Text.Json;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.API.Tests.Rutas;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Tests.Mensajeria;

using static BaseConDosOperadores;

// Eventos de bulto en el outbox (entrega parcial, ver docs/mensajeria.md): cada bulto que cambia de
// estado deja su mensaje en el mismo guardado que el cambio, con el código del bulto y el motivo.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class EventosBultoTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Una_entrega_parcial_deja_un_evento_por_bulto_con_su_codigo_y_motivo()
    {
        var envioId = await EnvioEnTransitoAsync(bultos: 3);
        var danado = await MotivoAsync("Bulto dañado (eventos)");

        await using (var db = baseDeDatos.CrearContexto(OperadorA, correlacion: "parcial-1"))
        {
            var envio = await db.Envios.Include(e => e.Bultos).SingleAsync(e => e.Id == envioId);
            var bultos = envio.Bultos.OrderBy(b => b.Id).ToList();

            envio.RegistrarResultadoBultos(
            [
                Resultado(bultos[0], entregado: true),
                Resultado(bultos[1], entregado: true),
                Resultado(bultos[2], entregado: false, danado)
            ]);
            await db.SaveChangesAsync();
        }

        var mensajes = await MensajesDeAsync(envioId);
        var deBultos = mensajes.Where(m => m.Tipo == TiposEvento.BultoEstadoCambiado).ToList();
        var codigos = await CodigosAsync(envioId);

        Assert.Equal(3, deBultos.Count);
        Assert.Equal(codigos, deBultos.Select(m => m.CodigoBulto));
        Assert.All(deBultos, m => Assert.Equal("Pendiente", m.EstadoAnterior));
        Assert.Equal(["Entregado", "Entregado", "NoEntregado"], deBultos.Select(m => m.Estado));
        Assert.Equal([null, null, "Bulto dañado (eventos)"], deBultos.Select(m => m.MotivoNoEntrega));
        Assert.All(deBultos, m => Assert.Equal(("Repartidor", "parcial-1"), (m.Origen, m.CorrelacionId)));

        // El cambio del envío sale primero, y después el de cada bulto
        var delEnvio = mensajes.Last(m => m.Tipo == TiposEvento.EnvioEstadoCambiado);
        Assert.Equal("EntregadoParcialmente", delEnvio.Estado);
        Assert.True(mensajes.IndexOf(delEnvio) < mensajes.IndexOf(deBultos[0]));
    }

    [Fact]
    public async Task Los_eventos_de_bulto_tienen_el_origen_de_quien_los_cambio_aunque_despues_actue_el_sistema()
    {
        var envioId = await EnvioEnTransitoAsync(bultos: 2);
        var motivo = await MotivoAsync("Rechazado (eventos)");

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var envio = await db.Envios.Include(e => e.Bultos).SingleAsync(e => e.Id == envioId);
            var bultos = envio.Bultos.OrderBy(b => b.Id).ToList();

            envio.RegistrarResultadoBultos([Resultado(bultos[0], entregado: true), Resultado(bultos[1], false, motivo)]);
            // Como la regla de entrega parcial, en el mismo guardado
            envio.CambiarEstado(EstadoEnvio.Reprogramado, "Sistema");
            await db.SaveChangesAsync();
        }

        var deBultos = (await MensajesDeAsync(envioId)).Where(m => m.Tipo == TiposEvento.BultoEstadoCambiado).ToList();

        Assert.Equal(2, deBultos.Count);
        Assert.All(deBultos, m => Assert.Equal("Repartidor", m.Origen));
    }

    [Fact]
    public async Task Un_cambio_que_no_toca_bultos_no_deja_eventos_de_bulto()
    {
        var envioId = await EnvioEnTransitoAsync(bultos: 2);

        Assert.DoesNotContain(await MensajesDeAsync(envioId), m => m.Tipo == TiposEvento.BultoEstadoCambiado);
    }

    [Fact]
    public async Task Si_un_guardado_falla_y_se_reintenta_cada_evento_de_bulto_sale_una_sola_vez()
    {
        var envioId = await EnvioEnTransitoAsync(bultos: 2);

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var envio = await db.Envios.Include(e => e.Bultos).SingleAsync(e => e.Id == envioId);
            envio.RegistrarResultadoBultos(envio.Bultos.Select(b => Resultado(b, entregado: true)).ToList());

            // Un dato inválido hace fallar el primer guardado
            var bulto = envio.Bultos.First();
            var descripcion = bulto.Descripcion;
            bulto.Descripcion = new string('x', 501);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

            bulto.Descripcion = descripcion;
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, (await MensajesDeAsync(envioId)).Count(m => m.Tipo == TiposEvento.BultoEstadoCambiado));
    }

    [Fact]
    public void Los_eventos_de_envio_no_cambian_su_JSON()
    {
        // Los comercios ya reciben este formato: los campos de bulto no aparecen ni vacíos
        var evento = new EstadoEnvioCambiado(
            Guid.NewGuid(), TiposEvento.EnvioEstadoCambiado, EstadoEnvioCambiado.VersionActual, DateTime.UtcNow,
            OperadorA, ComercioCompartido, 1, "CODIGO", "EnDeposito", "Admitido", "Backoffice", true, null);

        var json = JsonSerializer.Serialize(evento);

        Assert.DoesNotContain("CodigoBulto", json);
        Assert.DoesNotContain("MotivoNoEntrega", json);
    }

    // Un envío en tránsito, en una ruta despachada, con esa cantidad de bultos
    private async Task<int> EnvioEnTransitoAsync(int bultos)
    {
        var (_, _, envios) = await DatosDeRutas.CrearRutaDespachadaAsync(baseDeDatos, OperadorA, bultosPorEnvio: bultos);
        return envios[0];
    }

    private static ResultadoBultoIntento Resultado(Bulto bulto, bool entregado, int? motivo = null) => new()
    {
        BultoId = bulto.Id,
        Entregado = entregado,
        MotivoNoEntregaId = motivo
    };

    private async Task<int> MotivoAsync(string nombre)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var motivo = await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Nombre == nombre);

        if (motivo is null)
        {
            motivo = new MotivoNoEntrega { Nombre = nombre };
            db.MotivosNoEntrega.Add(motivo);
            await db.SaveChangesAsync();
        }

        return motivo.Id;
    }

    private async Task<List<string>> CodigosAsync(int envioId)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        return await db.Bultos.Where(b => b.EnvioId == envioId).OrderBy(b => b.Id).Select(b => b.Codigo).ToListAsync();
    }

    private record Mensaje(
        string Tipo, string Estado, string? EstadoAnterior, string? CodigoBulto, string? MotivoNoEntrega,
        string Origen, string? CorrelacionId);

    // Lee el outbox como superusuario (la aplicación no puede), en el orden en que se publicaría
    private async Task<List<Mensaje>> MensajesDeAsync(int envioId)
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            SELECT "Tipo", "Estado", "EstadoAnterior", "CodigoBulto", "MotivoNoEntrega", "Origen", "CorrelacionId"
            FROM "MensajesOutbox" WHERE "EnvioId" = @envio ORDER BY "FechaCreacion"
            """, conexion);
        comando.Parameters.AddWithValue("envio", envioId);

        await using var lector = await comando.ExecuteReaderAsync();
        var mensajes = new List<Mensaje>();

        string? Texto(int i) => lector.IsDBNull(i) ? null : lector.GetString(i);

        while (await lector.ReadAsync())
            mensajes.Add(new Mensaje(lector.GetString(0), lector.GetString(1), Texto(2), Texto(3), Texto(4),
                lector.GetString(5), Texto(6)));

        return mensajes;
    }
}
