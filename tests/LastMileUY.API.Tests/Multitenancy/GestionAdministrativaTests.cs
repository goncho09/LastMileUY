
using LastMileUY.API.Modules.Comercios.Application;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.Contracts.Comercios;
using LastMileUY.Contracts.Operadores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LastMileUY.API.Tests.Multitenancy;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class GestionAdministrativaTests(
    BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Crear_y_editar_operador_persiste_los_cambios()
    {
        var nombre = $"Operador Test {Guid.NewGuid():N}";

        int id;

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionOperadoresService(db);

            var creado = await servicio.CrearAsync(
                new GuardarOperadorAdminRequest(
                    nombre, "#336699", null),
                CancellationToken.None);

            id = creado.Id;

            Assert.True(id > 0);
            Assert.Equal(nombre, creado.Nombre);

            var editado = await servicio.EditarAsync(
                id,
                new GuardarOperadorAdminRequest(
                    "Operador Editado", "#112233", null),
                CancellationToken.None);

            Assert.True(editado);
        }

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionOperadoresService(db);
            var obtenido = await servicio.ObtenerAsync(
                id, CancellationToken.None);

            Assert.NotNull(obtenido);
            Assert.Equal("Operador Editado", obtenido.Nombre);
            Assert.Equal("#112233", obtenido.ColorPrimario);
        }
    }

    [Fact]
    public async Task Crear_y_editar_comercio_persiste_los_cambios()
    {
        var nombre = $"Comercio Test {Guid.NewGuid():N}";

        int id;

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionComerciosService(db);

            var creado = await servicio.CrearAsync(
                new GuardarComercioAdminRequest(
                    nombre,
                    "Razón Social Test",
                    null,
                    "test@ejemplo.com",
                    "099123456"),
                CancellationToken.None);

            id = creado.Id;

            Assert.True(id > 0);

            var editado = await servicio.EditarAsync(
                id,
                new GuardarComercioAdminRequest(
                    "Comercio Editado",
                    "Nueva Razón Social",
                    null,
                    "nuevo@ejemplo.com",
                    "098123456"),
                CancellationToken.None);

            Assert.True(editado);
        }

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionComerciosService(db);
            var obtenido = await servicio.ObtenerAsync(
                id, CancellationToken.None);

            Assert.NotNull(obtenido);
            Assert.Equal("Comercio Editado", obtenido.Nombre);
            Assert.Equal("nuevo@ejemplo.com", obtenido.Email);
        }
    }

    [Fact]
    public async Task Vincular_desvincular_y_revincular_no_duplica_registros()
    {
        int comercioId;
        int operadorId;

        // Creamos datos propios para no modificar los
        // operadores y comercios de las demás pruebas.
        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var operadores = new GestionOperadoresService(db);
            var comercios = new GestionComerciosService(db);

            operadorId = (await operadores.CrearAsync(
                new GuardarOperadorAdminRequest(
                    $"Operador Vinculo {Guid.NewGuid():N}",
                    null, null),
                CancellationToken.None)).Id;

            comercioId = (await comercios.CrearAsync(
                new GuardarComercioAdminRequest(
                    $"Comercio Vinculo {Guid.NewGuid():N}",
                    null, null, null, null),
                CancellationToken.None)).Id;
        }

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionComerciosService(db);

            Assert.True(await servicio.VincularAsync(
                comercioId, operadorId, CancellationToken.None));

            // Vincular nuevamente no debe duplicar.
            Assert.True(await servicio.VincularAsync(
                comercioId, operadorId, CancellationToken.None));
        }

        await VerificarVinculoAsync(
            comercioId, operadorId, true, 1);

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionComerciosService(db);

            Assert.True(await servicio.DesvincularAsync(
                comercioId, operadorId, CancellationToken.None));
        }

        // El registro permanece, pero inactivo.
        await VerificarVinculoAsync(
            comercioId, operadorId, false, 1);

        await using (var db = baseDeDatos.CrearContexto(null))
        {
            var servicio = new GestionComerciosService(db);

            Assert.True(await servicio.VincularAsync(
                comercioId, operadorId, CancellationToken.None));
        }

        // Reactivar tampoco debe crear otro registro.
        await VerificarVinculoAsync(
            comercioId, operadorId, true, 1);
    }

    private async Task VerificarVinculoAsync(
        int comercioId,
        int operadorId,
        bool activo,
        int cantidad)
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var vinculos = await db.OperadoresComercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v =>
                v.ComercioId == comercioId &&
                v.OperadorId == operadorId)
            .ToListAsync();

        Assert.Equal(cantidad, vinculos.Count);
        Assert.Equal(activo, Assert.Single(vinculos).Activo);
    }

    [Fact]
    public async Task Comercio_con_rut_duplicado_es_rechazado()
    {
        var rut = Guid.NewGuid().ToString("N")[..12];

        await using var db = baseDeDatos.CrearContexto(null);
        var servicio = new GestionComerciosService(db);

        await servicio.CrearAsync(
            new GuardarComercioAdminRequest(
                "Comercio Original",
                null, rut, null, null),
            CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servicio.CrearAsync(
                new GuardarComercioAdminRequest(
                    "Comercio Duplicado",
                    null, rut, null, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Operador_con_color_invalido_es_rechazado()
    {
        await using var db = baseDeDatos.CrearContexto(null);
        var servicio = new GestionOperadoresService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            servicio.CrearAsync(
                new GuardarOperadorAdminRequest(
                    "Operador Test", "azul", null),
                CancellationToken.None));
    }
}
