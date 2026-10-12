namespace LastMileUY.API.Tests.Multitenancy;

using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static BaseConDosOperadores;

// Pruebas del ADR-01 que completan las de AislamientoEntreOperadoresTests:
// las tablas hijas (sin OperadorId) y el segundo nivel, el comercio.
// En cada caso se prueban las dos barreras: el filtro de EF y, con IgnoreQueryFilters, RLS.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AislamientoEntreComerciosTests(BaseConDosOperadores baseDeDatos)
{
    // --- Tablas hijas: se ven solo si se ve su padre ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Los_intentos_de_entrega_de_un_envio_ajeno_no_se_ven(bool salteandoEF)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var consulta = salteandoEF ? db.IntentosEntrega.IgnoreQueryFilters() : db.IntentosEntrega;

        var intentos = await consulta.ToListAsync();

        Assert.Contains(intentos, i => i.EnvioId == EnvioDeA);
        Assert.DoesNotContain(intentos, i => i.EnvioId == EnvioDeB);
    }

    [Fact]
    public async Task La_base_no_deja_colgar_un_intento_de_entrega_de_un_envio_ajeno()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "IntentosEntrega"("EnvioId", "NumeroIntento", "FechaHora", "FechaRecepcion", "Exitoso")
                VALUES ({EnvioDeB}, 2, now(), now(), true)
            """));

        Assert.Equal("42501", error.SqlState);
    }

    // --- Destinatarios: son del comercio ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task El_operador_solo_ve_destinatarios_de_los_comercios_con_los_que_trabaja(bool salteandoEF)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var consulta = salteandoEF ? db.Destinatarios.IgnoreQueryFilters() : db.Destinatarios;

        var ids = await consulta.Select(d => d.Id).ToListAsync();

        Assert.Contains(1, ids);
        Assert.Contains(DestinatarioComercio2, ids);
        Assert.DoesNotContain(DestinatarioComercio3, ids);
    }

    [Fact]
    public async Task Las_direcciones_de_un_destinatario_ajeno_no_se_ven_ni_en_la_base()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var direcciones = await db.Direcciones.IgnoreQueryFilters().ToListAsync();

        Assert.DoesNotContain(direcciones, d => d.DestinatarioId == DestinatarioComercio3);
    }

    [Fact]
    public async Task El_operador_no_puede_cargar_destinatarios_a_un_comercio_con_el_que_no_trabaja()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "Destinatarios"("ComercioId", "Nombre", "Apellido", "FechaCreacion")
                VALUES ({ComercioSoloDeB}, 'Intruso', 'X', now())
            """));

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task El_operador_solo_ve_los_comercios_con_los_que_trabaja()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var ids = await db.Comercios.Select(c => c.Id).ToListAsync();

        Assert.Contains(ComercioCompartido, ids);
        Assert.Contains(ComercioSoloDeA, ids);
        Assert.DoesNotContain(ComercioSoloDeB, ids);
    }

    // --- Segundo nivel: un usuario de comercio solo ve lo de su comercio ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Un_comercio_solo_ve_sus_envios_dentro_del_operador(bool salteandoEF)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var consulta = salteandoEF ? db.Envios.IgnoreQueryFilters() : db.Envios;

        var envios = await consulta.ToListAsync();

        Assert.Contains(envios, e => e.Id == EnvioDeAComercio2);
        Assert.DoesNotContain(envios, e => e.Id == EnvioDeA);
        Assert.All(envios, e => Assert.Equal(ComercioSoloDeA, e.ComercioId));
    }

    [Fact]
    public async Task El_usuario_del_operador_ve_los_envios_de_todos_sus_comercios()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var ids = await db.Envios.Select(e => e.Id).ToListAsync();

        Assert.Contains(EnvioDeA, ids);
        Assert.Contains(EnvioDeAComercio2, ids);
    }

    [Fact]
    public async Task Un_comercio_no_ve_los_destinatarios_de_otro_comercio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        var ids = await db.Destinatarios.IgnoreQueryFilters().Select(d => d.Id).ToListAsync();

        Assert.Equal([1], ids);
    }

    [Fact]
    public async Task Un_comercio_no_ve_los_bultos_de_envios_de_otro_comercio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);

        var bultos = await db.Bultos.IgnoreQueryFilters().ToListAsync();

        Assert.DoesNotContain(bultos, b => b.EnvioId == EnvioDeA);
    }

    [Fact]
    public async Task La_base_no_deja_a_un_comercio_modificar_envios_de_otro_comercio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioCompartido);

        var filas = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Envios\" SET \"FechaEntrega\" = now() WHERE \"Id\" = {EnvioDeAComercio2}");

        Assert.Equal(0, filas);
    }

    [Fact]
    public async Task Lo_que_crea_un_comercio_queda_a_su_nombre()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);
        var envio = NuevoEnvio(comercioId: 0, destinatarioId: DestinatarioComercio2);

        db.Envios.Add(envio);
        await db.SaveChangesAsync();

        Assert.Equal(ComercioSoloDeA, envio.ComercioId);
        Assert.Equal(OperadorA, envio.OperadorId);
    }

    [Fact]
    public async Task Un_comercio_no_puede_crear_envios_de_otro_comercio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, ComercioSoloDeA);

        db.Envios.Add(NuevoEnvio(comercioId: ComercioCompartido, destinatarioId: 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task No_se_puede_pasar_un_envio_a_otro_comercio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var envio = await db.Envios.SingleAsync(e => e.Id == EnvioDeAComercio2);

        envio.ComercioId = ComercioCompartido;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    private static Envio NuevoEnvio(int comercioId, int destinatarioId)
    {
        var envio = new Envio { ComercioId = comercioId, DestinatarioId = destinatarioId };
        envio.AsignarDireccionEntrega(new DireccionEntrega(
            "Ejido", "55", null, "Montevideo", "Montevideo", null, null));
        return envio;
    }
}
