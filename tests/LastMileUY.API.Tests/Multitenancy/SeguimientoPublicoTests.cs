namespace LastMileUY.API.Tests.Multitenancy;

using LastMileUY.API.Modules.Seguimiento.Application;
using Microsoft.EntityFrameworkCore;
using static BaseConDosOperadores;

// Seguimiento público (docs/seguimiento-publico.md): sin sesión, con el código se ve ese envío y nada más.
// Corren sin operador, con el usuario de la aplicación y la política seguimiento_publico de la base.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class SeguimientoPublicoTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Con_el_codigo_se_ve_el_envio_sin_iniciar_sesion()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var envio = await new SeguimientoPublicoService(db).ObtenerAsync("CODIGOB");

        Assert.NotNull(envio);
        Assert.Equal("CODIGOB", envio.CodigoSeguimiento);
        Assert.Equal("Operador B", envio.Operador.Nombre);
        Assert.Equal("Montevideo", envio.Ciudad);
    }

    [Fact]
    public async Task El_codigo_se_acepta_en_minusculas_y_con_espacios()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        var envio = await new SeguimientoPublicoService(db).ObtenerAsync("  codigoa ");

        Assert.Equal("CODIGOA", envio?.CodigoSeguimiento);
    }

    [Theory]
    [InlineData("NOEXISTE")]
    [InlineData("AB")]
    [InlineData("CODIGOA' OR '1'='1")]
    public async Task Un_codigo_que_no_existe_o_mal_escrito_no_devuelve_nada(string codigo)
    {
        await using var db = baseDeDatos.CrearContexto(null);

        Assert.Null(await new SeguimientoPublicoService(db).ObtenerAsync(codigo));
    }

    // --- La política de la base ---

    [Fact]
    public async Task La_base_solo_deja_ver_el_envio_del_codigo_cargado()
    {
        await using var db = baseDeDatos.CrearContexto(null);
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await CargarCodigoAsync(db, "CODIGOA");

        // Sin el filtro de EF: lo que devuelve es lo que deja pasar la base
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

        // Misma conexión antes y después, como cuando vuelve al pool y la usa otro pedido
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
        // El código abre el envío solo por la política de seguimiento; el filtro de EF del
        // operador sigue aplicando en el resto de la aplicación
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        await using var transaccion = await db.Database.BeginTransactionAsync();
        await CargarCodigoAsync(db, "CODIGOB");

        Assert.DoesNotContain(await db.Envios.ToListAsync(), e => e.Id == EnvioDeB);
    }

    // Lo mismo que hace SeguimientoPublicoService antes de consultar
    private static Task CargarCodigoAsync(DbContext db, string codigo) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.codigo_seguimiento', {codigo}, true)");
}
