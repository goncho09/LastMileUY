using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Seguimiento.Application;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Tests.Multitenancy;

using static BaseConDosOperadores;

[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class SeguimientoPublicoTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Con_el_codigo_se_ve_el_envio_sin_iniciar_sesion()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var envio = await new SeguimientoPublicoService(db, new FuncionalidadesService(db)).ObtenerAsync("CODIGOB");

        Assert.NotNull(envio);
        Assert.Equal("CODIGOB", envio.CodigoSeguimiento);
        Assert.Equal("Operador B", envio.Operador.Nombre);
        Assert.Equal("Montevideo", envio.Ciudad);
    }

    [Fact]
    public async Task El_codigo_se_acepta_en_minusculas_y_con_espacios()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var envio = await new SeguimientoPublicoService(db, new FuncionalidadesService(db)).ObtenerAsync("  codigoa ");

        Assert.Equal("CODIGOA", envio?.CodigoSeguimiento);
    }

    [Theory]
    [InlineData("NOEXISTE")]
    [InlineData("AB")]
    [InlineData("CODIGOA' OR '1'='1")]
    public async Task Un_codigo_que_no_existe_o_mal_escrito_no_devuelve_nada(string codigo)
    {
        await using var db = baseDeDatos.CrearContexto(null);

        Assert.Null(await new SeguimientoPublicoService(db, new FuncionalidadesService(db)).ObtenerAsync(codigo));
    }

    [Fact]
    public async Task La_base_solo_deja_ver_el_envio_del_codigo_cargado()
    {
        await using var db = baseDeDatos.CrearContexto(null);
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await CargarCodigoAsync(db, "CODIGOA");

        var envios = await db.Envios.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(EnvioDeA, Assert.Single(envios).Id);
    }

    [Fact]
    public async Task Con_el_codigo_no_se_puede_modificar_el_envio()
    {
        await using var db = baseDeDatos.CrearContexto(null);
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await CargarCodigoAsync(db, "CODIGOA");

        var filas = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Envios\" SET \"FechaEntrega\" = now() WHERE \"Id\" = {EnvioDeA}");

        Assert.Equal(0, filas);
    }

    [Fact]
    public async Task El_codigo_no_queda_cargado_al_terminar_la_transaccion()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        await db.Database.OpenConnectionAsync();

        await using (var transaccion = await db.Database.BeginTransactionAsync())
        {
            await CargarCodigoAsync(db, "CODIGOA");
            Assert.NotEmpty(await db.Envios.IgnoreQueryFilters().ToListAsync());
            await transaccion.CommitAsync();
        }

        Assert.Empty(await db.Envios.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Un_operador_con_el_codigo_de_otro_no_ve_su_envio()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await CargarCodigoAsync(db, "CODIGOB");

        Assert.DoesNotContain(await db.Envios.ToListAsync(), e => e.Id == EnvioDeB);
    }

    private static Task CargarCodigoAsync(DbContext db, string codigo) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.codigo_seguimiento', {codigo}, true)");
}
