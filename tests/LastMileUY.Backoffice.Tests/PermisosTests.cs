namespace LastMileUY.Backoffice.Tests;

using System.Security.Claims;
using LastMileUY.Backoffice.Pages;
using LastMileUY.Backoffice.Pages.Envios;
using LastMileUY.Backoffice.Servicios;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

public class PermisosTests
{
    [Theory]
    [InlineData("Operador", true)]
    [InlineData("Despachador", true)]
    [InlineData("Repartidor", false)]
    [InlineData("Comercio", false)]
    [InlineData("Administrador", true)]
    public void Solo_los_perfiles_del_Backoffice_pueden_entrar(string rol, bool puede)
    {
        Assert.Equal(puede, Permisos.PuedeEntrar([rol]));
    }

    [Fact]
    public void Sin_roles_no_se_entra()
    {
        Assert.False(Permisos.PuedeEntrar([]));
    }

    [Theory]
    [InlineData("Operador", true, true)]
    [InlineData("Despachador", true, false)]
    [InlineData("Repartidor", false, false)]
    public void Cada_perfil_ve_sus_acciones(string rol, bool cambiaEstado, bool veConfiguracion)
    {
        var usuario = Usuario(rol);

        Assert.Equal(cambiaEstado, Permisos.PuedeCambiarEstado(usuario));
        Assert.Equal(veConfiguracion, Permisos.PuedeVerConfiguracion(usuario));
    }
    
    [Fact]
    public void El_administrador_solo_puede_administrar_mensajeria()
    {
        var usuario = Usuario("Administrador");

        Assert.True(Permisos.PuedeEntrar(["Administrador"]));
        Assert.True(Permisos.PuedeAdministrarMensajeria(usuario));

        Assert.False(Permisos.PuedeCambiarEstado(usuario));
        Assert.False(Permisos.PuedeVerConfiguracion(usuario));
    }

    [Fact]
    public void El_perfil_se_muestra_con_nombre()
    {
        Assert.Equal("Despachador", Permisos.Perfil(Usuario("Despachador")));
        Assert.Equal("Operador y Despachador", Permisos.Perfil(Usuario("Operador", "Despachador")));
        Assert.Equal("Sin perfil", Permisos.Perfil(Usuario()));
    }

    [Fact]
    public async Task Sin_permiso_el_cambio_de_estado_no_llega_a_la_api()
    {
        var api = new EnviosApiDePrueba();
        var antes = await EnvioQuePuedeCambiarAsync(api);
        var pagina = Pagina(api, Usuario("Repartidor"));

        await pagina.OnPostCambiarEstadoAsync(antes.Id, antes.EstadosSiguientes[0], null, CancellationToken.None);

        Assert.Equal(antes.Estado, (await api.ObtenerAsync(antes.Id))!.Estado);
        Assert.Equal("Tu perfil no puede cambiar el estado de los envíos.", pagina.ErrorCambio);
    }

    [Fact]
    public async Task Con_permiso_el_cambio_de_estado_se_hace()
    {
        var api = new EnviosApiDePrueba();
        var antes = await EnvioQuePuedeCambiarAsync(api);
        var pagina = Pagina(api, Usuario("Despachador"));

        await pagina.OnPostCambiarEstadoAsync(antes.Id, antes.EstadosSiguientes[0], null, CancellationToken.None);

        Assert.Equal(antes.EstadosSiguientes[0], (await api.ObtenerAsync(antes.Id))!.Estado);
        Assert.Null(pagina.ErrorCambio);
    }

    [Fact]
    public async Task Un_repartidor_no_puede_iniciar_sesion_en_el_Backoffice()
    {
        var login = new LoginModel(new AutenticacionFija(["Repartidor"]), NullLogger<LoginModel>.Instance)
        {
            Email = "repartidor@prueba.test",
            Password = "clave",
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        var resultado = await login.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(resultado);
        Assert.Equal("Tu perfil no tiene acceso al Backoffice.", login.Error);
    }

    private class AutenticacionFija(string[] roles) : IAutenticacionApi
    {
        public Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default) =>
            Task.FromResult<SesionIniciada?>(new SesionIniciada("Usuario", email, 1, "cookie", null, roles));
    }

    // Un envío de los datos de prueba que todavía no está en un estado final
    private static async Task<Contracts.Envios.EnvioDetalleDto> EnvioQuePuedeCambiarAsync(IEnviosApi api)
    {
        for (var id = 1; ; id++)
        {
            var envio = await api.ObtenerAsync(id) ?? throw new InvalidOperationException("No hay envíos que puedan cambiar.");

            if (envio.EstadosSiguientes.Count > 0)
            {
                return envio;
            }
        }
    }

    private static ClaimsPrincipal Usuario(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "prueba"));

    private static DetalleModel Pagina(IEnviosApi api, ClaimsPrincipal usuario)
    {
        var http = new DefaultHttpContext { User = usuario };

        return new DetalleModel(api, NullLogger<DetalleModel>.Instance)
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataEnMemoria())
        };
    }

    private class TempDataEnMemoria : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
