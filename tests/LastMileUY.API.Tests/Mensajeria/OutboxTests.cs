namespace LastMileUY.API.Tests.Mensajeria;

using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// El outbox contra la base real (ver docs/mensajeria.md): cada evento de un envío deja su mensaje en
// el mismo guardado, con los datos del contrato, y cada usuario de la base hace solo lo que le toca.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class OutboxTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task El_alta_de_un_envio_deja_el_evento_admitido_con_el_id_que_le_dio_la_base()
    {
        var envioId = await CrearEnvioAsync(correlacion: "alta-1");

        var mensaje = Assert.Single(await MensajesDeAsync(envioId));
        Assert.Equal(TiposEvento.EnvioAdmitido, mensaje.Tipo);
        Assert.Equal(envioId, mensaje.EnvioId);
        Assert.Equal(OperadorA, mensaje.OperadorId);
        Assert.Equal(ComercioCompartido, mensaje.ComercioId);
        Assert.Equal("Admitido", mensaje.Estado);
        Assert.Null(mensaje.EstadoAnterior);
        Assert.Equal("alta-1", mensaje.CorrelacionId);
        Assert.Null(mensaje.FechaPublicacion);
    }

    [Fact]
    public async Task Un_cambio_de_estado_deja_el_estado_anterior_y_el_nuevo()
    {
        var envioId = await CrearEnvioAsync();

        await using (var db = baseDeDatos.CrearContexto(OperadorA, correlacion: "cambio-1"))
        {
            var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
            envio.CambiarEstado(EstadoEnvio.EnDeposito, "Backoffice", "Llamar antes, nota interna");
            await db.SaveChangesAsync();
        }

        var mensaje = (await MensajesDeAsync(envioId)).Last();
        Assert.Equal(TiposEvento.EnvioEstadoCambiado, mensaje.Tipo);
        Assert.Equal("Admitido", mensaje.EstadoAnterior);
        Assert.Equal("EnDeposito", mensaje.Estado);
        Assert.Equal("Backoffice", mensaje.Origen);
        Assert.Equal("cambio-1", mensaje.CorrelacionId);
    }

    [Fact]
    public async Task Varios_cambios_en_un_guardado_salen_en_orden_y_encadenados()
    {
        var envioId = await CrearEnvioAsync();

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            envio.CambiarEstado(EstadoEnvio.AsignadoARuta);
            await db.SaveChangesAsync();
        }

        var cambios = (await MensajesDeAsync(envioId)).Skip(1).ToList();
        Assert.Equal([("Admitido", "EnDeposito"), ("EnDeposito", "AsignadoARuta")],
            cambios.Select(m => (m.EstadoAnterior, m.Estado)));
    }

    [Fact]
    public async Task Si_el_cambio_no_se_guarda_no_queda_el_evento()
    {
        var envioId = await CrearEnvioAsync();

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            await using var transaccion = await db.Database.BeginTransactionAsync();
            var envio = await db.Envios.SingleAsync(e => e.Id == envioId);
            envio.CambiarEstado(EstadoEnvio.EnDeposito);
            await db.SaveChangesAsync();
            await transaccion.RollbackAsync();
        }

        Assert.Single(await MensajesDeAsync(envioId));
    }

    [Fact]
    public async Task Si_un_guardado_falla_y_se_reintenta_el_evento_sale_una_sola_vez()
    {
        var envioId = await CrearEnvioAsync();

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var envio = await db.Envios.SingleAsync(e => e.Id == envioId);

            // La descripción no entra en la columna (1000 caracteres): la base rechaza el guardado
            envio.CambiarEstado(EstadoEnvio.EnDeposito, descripcion: new string('x', 1001));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            envio.Eventos.Last().Descripcion = "Llegó al depósito";
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, (await MensajesDeAsync(envioId)).Count);
    }

    [Fact]
    public async Task La_aplicacion_no_puede_leer_el_outbox()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.MensajesOutbox.CountAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task El_publicador_solo_tiene_acceso_al_outbox()
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaPublicador);
        await conexion.OpenAsync();

        await using var outbox = new NpgsqlCommand("SELECT count(*) FROM \"MensajesOutbox\"", conexion);
        await outbox.ExecuteScalarAsync();

        await using var envios = new NpgsqlCommand("SELECT count(*) FROM \"Envios\"", conexion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => envios.ExecuteScalarAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    private async Task<int> CrearEnvioAsync(string? correlacion = null)
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA, correlacion: correlacion);
        var envio = new Envio { OperadorId = OperadorA, ComercioId = ComercioCompartido, DestinatarioId = 1 };
        envio.AsignarDireccionEntrega(new DireccionEntrega(
            "Rivera", "1234", null, "Montevideo", "Montevideo", null, null));
        envio.RegistrarAdmision("Backoffice");

        db.Envios.Add(envio);
        await db.SaveChangesAsync();

        return envio.Id;
    }

    private record Mensaje(
        string Tipo, int EnvioId, int OperadorId, int ComercioId, string Estado, string? EstadoAnterior,
        string Origen, string? CorrelacionId, DateTime? FechaPublicacion);

    // Lee el outbox como superusuario (la aplicación no puede), en el orden en que se publicaría
    private async Task<List<Mensaje>> MensajesDeAsync(int envioId)
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            SELECT "Tipo", "EnvioId", "OperadorId", "ComercioId", "Estado", "EstadoAnterior", "Origen",
                   "CorrelacionId", "FechaPublicacion"
            FROM "MensajesOutbox" WHERE "EnvioId" = @envio ORDER BY "FechaCreacion"
            """, conexion);
        comando.Parameters.AddWithValue("envio", envioId);

        await using var lector = await comando.ExecuteReaderAsync();
        var mensajes = new List<Mensaje>();

        while (await lector.ReadAsync())
        {
            mensajes.Add(new Mensaje(
                lector.GetString(0), lector.GetInt32(1), lector.GetInt32(2), lector.GetInt32(3),
                lector.GetString(4), lector.IsDBNull(5) ? null : lector.GetString(5), lector.GetString(6),
                lector.IsDBNull(7) ? null : lector.GetString(7),
                lector.IsDBNull(8) ? null : lector.GetDateTime(8)));
        }

        return mensajes;
    }
}
