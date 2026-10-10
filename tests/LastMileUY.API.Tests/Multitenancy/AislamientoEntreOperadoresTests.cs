namespace LastMileUY.API.Tests.Multitenancy;

using LastMileUY.API.Modules.Operadores.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static BaseConDosOperadores;

// Pruebas del ADR-01: un operador nunca ve ni toca datos de otro.
// Corren contra un PostgreSQL real (Docker), con el mismo usuario y las mismas políticas que la aplicación.
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class AislamientoEntreOperadoresTests(BaseConDosOperadores baseDeDatos)
{
    // --- Primera barrera: el filtro de EF ---

    [Fact]
    public async Task Un_operador_solo_ve_sus_envios()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var envios = await db.Envios.ToListAsync();

        Assert.Contains(envios, e => e.Id == EnvioDeA);
        Assert.DoesNotContain(envios, e => e.Id == EnvioDeB);
        Assert.All(envios, e => Assert.Equal(OperadorA, e.OperadorId));
    }

    [Fact]
    public async Task Buscar_un_envio_de_otro_operador_por_id_no_lo_encuentra()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var envio = await db.Envios.FirstOrDefaultAsync(e => e.Id == EnvioDeB);

        Assert.Null(envio);
    }

    [Fact]
    public async Task Sin_operador_no_se_ve_ningun_envio()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        Assert.Empty(await db.Envios.ToListAsync());
    }

    // --- Segunda barrera: RLS en la base ---

    [Fact]
    public async Task La_base_filtra_aunque_el_codigo_saltee_el_filtro_de_EF()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        // IgnoreQueryFilters simula el "olvido" de un programador
        var envios = await db.Envios.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(envios);
        Assert.All(envios, e => Assert.Equal(OperadorA, e.OperadorId));
    }

    [Fact]
    public async Task La_base_no_deja_modificar_un_envio_de_otro_operador()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var filas = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Envios\" SET \"FechaEntrega\" = now() WHERE \"Id\" = {EnvioDeB}");

        Assert.Equal(0, filas);
    }

    [Fact]
    public async Task La_base_no_deja_borrar_los_bultos_de_un_envio_ajeno()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var filas = await db.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"Bultos\" WHERE \"EnvioId\" = {EnvioDeB}");

        Assert.Equal(0, filas);
    }

    [Fact]
    public async Task La_base_no_deja_crear_datos_a_nombre_de_otro_operador()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO \"Zonas\"(\"OperadorId\", \"Nombre\", \"Activa\") VALUES ({OperadorB}, 'Intrusa', true)"));

        // 42501 = violación de una política de seguridad
        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task Los_bultos_de_un_envio_ajeno_no_se_ven()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var bultos = await db.Bultos.ToListAsync();

        Assert.Contains(bultos, b => b.EnvioId == EnvioDeA);
        Assert.DoesNotContain(bultos, b => b.EnvioId == EnvioDeB);
    }

    // --- Al guardar con EF ---

    [Fact]
    public async Task Lo_nuevo_queda_a_nombre_del_operador_actual()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorB);
        var zona = new Zona { Nombre = "Centro" };

        db.Zonas.Add(zona);
        await db.SaveChangesAsync();

        Assert.Equal(OperadorB, zona.OperadorId);
    }

    [Fact]
    public async Task No_se_puede_crear_para_otro_operador()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        db.Zonas.Add(new Zona { Nombre = "Ajena", OperadorId = OperadorB });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task No_se_puede_crear_sin_operador()
    {
        await using var db = baseDeDatos.CrearContexto(null);

        db.Zonas.Add(new Zona { Nombre = "Huerfana" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task No_se_puede_pasar_algo_a_otro_operador()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);
        var zona = new Zona { Nombre = "Norte" };
        db.Zonas.Add(zona);
        await db.SaveChangesAsync();

        zona.OperadorId = OperadorB;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    // --- Que nadie se olvide de RLS ---

    [Fact]
    public async Task Toda_tabla_con_OperadorId_tiene_RLS_forzado()
    {
        await using var conexion = new NpgsqlConnection(baseDeDatos.CadenaAdministrador);
        await conexion.OpenAsync();

        // Usuarios: el login necesita leerlos antes de saber el operador.
        // OperadoresComercios: el comercio la usa para elegir con qué operador trabajar.
        const string sql = """
            SELECT c.relname
            FROM pg_class c
            JOIN information_schema.columns col
              ON col.table_name = c.relname AND col.column_name = 'OperadorId'
            WHERE col.table_schema = 'public'
              AND c.relkind = 'r'
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
              AND c.relname NOT IN ('AspNetUsers', 'OperadoresComercios')
            """;

        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync();

        var sinRls = new List<string>();
        while (await lector.ReadAsync())
        {
            sinRls.Add(lector.GetString(0));
        }

        Assert.Empty(sinRls);
    }

    [Fact]
    public async Task El_usuario_de_la_aplicacion_no_se_saltea_RLS()
    {
        await using var db = baseDeDatos.CrearContexto(OperadorA);

        var salteaRls = await db.Database
            .SqlQueryRaw<bool>("SELECT (rolsuper OR rolbypassrls) AS \"Value\" FROM pg_roles WHERE rolname = current_user")
            .SingleAsync();

        Assert.False(salteaRls);
    }
}
