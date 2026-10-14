namespace LastMileUY.API.Tests.Envios;

using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Envios.Application;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Tests.Multitenancy;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Por dónde entró cada envío: el Backoffice tiene que poder ver lo que cargan los comercios desde su portal
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class CanalIngresoTests(BaseConDosOperadores baseDeDatos)
{
    [Theory]
    [InlineData("Comercio", CanalIngreso.Portal)]
    [InlineData("Operador", CanalIngreso.Api)]
    [InlineData("Despachador", CanalIngreso.Api)]
    public void El_canal_sale_del_perfil_de_quien_crea_el_envio(string rol, CanalIngreso esperado)
    {
        var usuario = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, rol)], "prueba"));

        Assert.Equal(esperado, CrearEnvioService.CanalDe(usuario));
    }

    [Fact]
    public async Task Un_envio_del_portal_queda_marcado_y_su_historial_lo_dice()
    {
        var id = await CrearAsync(CanalIngreso.Portal, "PED-PORTAL-1");

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var envio = await db.Envios.Include(e => e.Eventos).SingleAsync(e => e.Id == id);

        Assert.Equal(CanalIngreso.Portal, envio.Canal);
        Assert.Equal("Portal", Assert.Single(envio.Eventos).Origen);
    }

    [Fact]
    public async Task El_listado_filtra_por_canal()
    {
        var delPortal = await CrearAsync(CanalIngreso.Portal, "PED-PORTAL-2");
        var porApi = await CrearAsync(CanalIngreso.Api, "PED-API-2");

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var consultas = new ConsultasEnviosService(db);
        var soloPortal = await consultas.ListarAsync(1, 100, null, null, "portal");
        var soloApi = await consultas.ListarAsync(1, 100, null, null, "Api");

        Assert.Contains(soloPortal.Items, e => e.Id == delPortal);
        Assert.DoesNotContain(soloPortal.Items, e => e.Id == porApi);
        Assert.All(soloPortal.Items, e => Assert.Equal("Portal", e.Canal));
        Assert.Contains(soloApi.Items, e => e.Id == porApi);
        Assert.All(soloApi.Items, e => Assert.Equal("Api", e.Canal));
    }

    [Fact]
    public async Task El_detalle_muestra_el_canal()
    {
        var id = await CrearAsync(CanalIngreso.Portal, "PED-PORTAL-3");

        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var detalle = await new ConsultasEnviosService(db).ObtenerDetalleAsync(id);

        Assert.Equal("Portal", detalle?.Canal);
    }

    [Fact]
    public async Task Los_envios_cargados_antes_quedan_como_API()
    {
        // Los de la base de prueba se insertan por SQL sin canal: toman el valor por defecto de la columna
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var envio = await db.Envios.SingleAsync(e => e.Id == EnvioDeA);

        Assert.Equal(CanalIngreso.Api, envio.Canal);
    }

    [Fact]
    public async Task Un_canal_que_no_existe_es_un_error()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ConsultasEnviosService(db).ListarAsync(1, 20, null, null, "Fax"));
    }

    private async Task<int> CrearAsync(CanalIngreso canal, string referencia)
    {
        await VincularOperadorConComercioAsync();
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var request = new CrearEnvioRequest
        {
            ComercioId = ComercioCompartido, DestinatarioId = 1, DireccionId = 1, ReferenciaExterna = referencia
        };

        var creado = await new CrearEnvioService(db, new ContextoOperadorFijo(OperadorA))
            .EjecutarAsync(request, "usuario-1", canal);

        return creado.Id;
    }

    // El alta exige que el operador trabaje con el comercio
    private async Task VincularOperadorConComercioAsync()
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();
        await using var comando = new NpgsqlCommand($"""
            INSERT INTO "OperadoresComercios"("OperadorId", "ComercioId", "FechaVinculacion", "Activo")
                VALUES ({OperadorA}, {ComercioCompartido}, now(), true) ON CONFLICT DO NOTHING
            """, conexion);
        await comando.ExecuteNonQueryAsync();
    }
}
