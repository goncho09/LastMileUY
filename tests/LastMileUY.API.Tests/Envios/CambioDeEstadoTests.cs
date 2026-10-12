namespace LastMileUY.API.Tests.Envios;

using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Cambio de estado y alta de envíos contra la base real: historial, bultos, duplicados y concurrencia
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class CambioDeEstadoTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Cambiar_el_estado_lo_guarda_y_lo_deja_en_el_historial()
    {
        var id = await CrearEnvioAsync(OperadorA);

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            var existe = await new CambiarEstadoEnvioService(db).EjecutarAsync(
                id, new CambiarEstadoRequest { Estado = "EnDeposito", Descripcion = "Llegó al depósito" },
                "Backoffice", "usuario-1");

            Assert.True(existe);
        }

        await using var otra = baseDeDatos.CrearContexto(OperadorA);
        var envio = await otra.Envios.Include(e => e.Eventos).SingleAsync(e => e.Id == id);
        var evento = Assert.Single(envio.Eventos);

        Assert.Equal(EstadoEnvio.EnDeposito, envio.Estado);
        Assert.Equal("EnDeposito", evento.Tipo);
        Assert.Equal("Llegó al depósito", evento.Descripcion);
        Assert.Equal("Backoffice", evento.Origen);
        Assert.Equal("usuario-1", evento.ResponsableId);
    }

    [Fact]
    public async Task Una_transicion_que_no_esta_en_la_tabla_se_rechaza()
    {
        var id = await CrearEnvioAsync(OperadorA);
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<TransicionInvalidaException>(() => new CambiarEstadoEnvioService(db)
            .EjecutarAsync(id, new CambiarEstadoRequest { Estado = "Entregado" }, "Backoffice", null));
    }

    [Fact]
    public async Task Un_estado_que_no_existe_es_un_error()
    {
        var id = await CrearEnvioAsync(OperadorA);
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(() => new CambiarEstadoEnvioService(db)
            .EjecutarAsync(id, new CambiarEstadoRequest { Estado = "Volando" }, "Backoffice", null));
    }

    [Fact]
    public async Task No_se_puede_cambiar_el_estado_de_un_envio_de_otro_operador()
    {
        var idDeB = await CrearEnvioAsync(OperadorB);
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var existe = await new CambiarEstadoEnvioService(db).EjecutarAsync(
            idDeB, new CambiarEstadoRequest { Estado = "EnDeposito" }, "Backoffice", null);

        Assert.False(existe);
    }

    [Fact]
    public async Task Si_dos_usuarios_lo_cambian_a_la_vez_el_segundo_falla()
    {
        var id = await CrearEnvioAsync(OperadorA);

        // Los dos leen el envío en el mismo estado
        await using var primero = baseDeDatos.CrearContexto(OperadorA);
        await using var segundo = baseDeDatos.CrearContexto(OperadorA);
        var envio1 = await primero.Envios.SingleAsync(e => e.Id == id);
        var envio2 = await segundo.Envios.SingleAsync(e => e.Id == id);

        envio1.CambiarEstado(EstadoEnvio.EnDeposito, "Backoffice");
        await primero.SaveChangesAsync();

        // El segundo guarda sobre una versión vieja: xmin cambió y EF lo detecta
        envio2.CambiarEstado(EstadoEnvio.EnDeposito, "Backoffice");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
    }

    [Fact]
    public async Task El_alta_guarda_los_bultos_y_el_primer_evento()
    {
        await VincularOperadorConComercioAsync();
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var request = new CrearEnvioRequest
        {
            ComercioId = 1,
            DestinatarioId = 1,
            DireccionId = 1,
            Bultos =
            [
                new CrearBultoRequest { PesoKg = 2, LargoCm = 30, AnchoCm = 20, AltoCm = 10, Descripcion = "Caja" },
                new CrearBultoRequest { PesoKg = 1, LargoCm = 10, AnchoCm = 10, AltoCm = 10 }
            ]
        };

        var creado = await new CrearEnvioService(db, new ContextoOperadorFijo(OperadorA)).EjecutarAsync(request, "usuario-1");

        await using var otra = baseDeDatos.CrearContexto(OperadorA);
        var envio = await otra.Envios.Include(e => e.Bultos).Include(e => e.Eventos).SingleAsync(e => e.Id == creado.Id);

        Assert.Equal([$"{envio.CodigoSeguimiento}-1", $"{envio.CodigoSeguimiento}-2"],
            envio.Bultos.OrderBy(b => b.Codigo).Select(b => b.Codigo));
        var evento = Assert.Single(envio.Eventos);
        Assert.Equal("Admitido", evento.Tipo);
        Assert.Equal("usuario-1", evento.ResponsableId);
    }

    [Fact]
    public async Task Cargar_dos_veces_la_misma_referencia_es_un_duplicado()
    {
        await VincularOperadorConComercioAsync();
        var request = new CrearEnvioRequest
        {
            ComercioId = 1, DestinatarioId = 1, DireccionId = 1, ReferenciaExterna = "PED-DUPLICADO"
        };

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            await new CrearEnvioService(db, new ContextoOperadorFijo(OperadorA)).EjecutarAsync(request);
        }

        await using var otra = baseDeDatos.CrearContexto(OperadorA);
        await Assert.ThrowsAsync<EnvioDuplicadoException>(() =>
            new CrearEnvioService(otra, new ContextoOperadorFijo(OperadorA)).EjecutarAsync(request));
    }

    [Fact]
    public async Task Un_bulto_sin_peso_se_rechaza()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var request = new CrearEnvioRequest
        {
            ComercioId = 1, DestinatarioId = 1, DireccionId = 1,
            Bultos = [new CrearBultoRequest { PesoKg = 0, LargoCm = 10, AnchoCm = 10, AltoCm = 10 }]
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CrearEnvioService(db, new ContextoOperadorFijo(OperadorA)).EjecutarAsync(request));
    }

    private async Task<int> CrearEnvioAsync(int operadorId)
    {
        await using var db = baseDeDatos.CrearContexto(operadorId);
        var envio = new Envio { ComercioId = 1, DestinatarioId = 1 };
        envio.AsignarDireccionEntrega(new DireccionEntrega(
            "Rivera", "1234", null, "Montevideo", "Montevideo", null, null));

        db.Envios.Add(envio);
        await db.SaveChangesAsync();

        return envio.Id;
    }

    // El alta exige que el operador trabaje con el comercio
    private async Task VincularOperadorConComercioAsync()
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            INSERT INTO "OperadoresComercios"("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
                VALUES ({OperadorA}, 1, now(), true) ON CONFLICT DO NOTHING
            """, conexion);
        await comando.ExecuteNonQueryAsync();
    }
}
