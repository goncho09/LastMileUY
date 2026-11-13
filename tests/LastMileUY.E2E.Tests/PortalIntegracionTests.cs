namespace LastMileUY.E2E.Tests;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionPortalComercioEnNavegador.Nombre)]
public class PortalIntegracionTests(PortalComercioEnNavegador portal)
{
    [Fact]
    public async Task Crea_una_clave_que_sirve_contra_la_api_y_al_revocarla_deja_de_servir()
    {
        var pagina = await portal.IniciarSesionAsync();
        var nombre = $"Tienda {Random.Shared.Next(1000, 9999)}";

        await pagina.GotoAsync("/integracion");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Integración", Exact = true })).ToBeVisibleAsync();

        var crear = pagina.GetByRole(AriaRole.Button, new() { Name = "Crear clave" });
        await Expect(crear).ToBeDisabledAsync();

        await pagina.GetByLabel("Nombre", new() { Exact = true }).FillAsync(nombre);
        await crear.ClickAsync();

        var panel = pagina.GetByRole(AriaRole.Heading, new() { Name = "Clave creada" });
        await Expect(panel).ToBeVisibleAsync();
        var campo = pagina.GetByLabel("Clave de API", new() { Exact = true });
        await Expect(campo).ToHaveValueAsync(new Regex("^lmuy_\\S{40,}$"));
        var clave = await campo.InputValueAsync();

        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre });
        await Expect(fila).ToContainTextAsync("Activa");
        await Expect(fila).ToContainTextAsync("Nunca");
        await Expect(fila).ToContainTextAsync(clave[..13]);

        Assert.Equal(HttpStatusCode.OK, await ConsultarConClaveAsync(clave));

        await pagina.ReloadAsync();
        await Expect(panel).ToBeHiddenAsync();
        await Expect(pagina.GetByText(clave)).ToHaveCountAsync(0);
        await Expect(fila).Not.ToContainTextAsync("Nunca");

        await fila.GetByRole(AriaRole.Button, new() { Name = $"Revocar la clave {nombre}" }).ClickAsync();
        var confirmacion = pagina.GetByRole(AriaRole.Heading, new() { Name = $"¿Revocar la clave «{nombre}»?" });
        await Expect(confirmacion).ToBeVisibleAsync();
        await pagina.Locator("section", new() { Has = confirmacion }).GetByRole(AriaRole.Button, new() { Name = "Cancelar" }).ClickAsync();
        await Expect(confirmacion).ToBeHiddenAsync();
        Assert.Equal(HttpStatusCode.OK, await ConsultarConClaveAsync(clave));

        await fila.GetByRole(AriaRole.Button, new() { Name = $"Revocar la clave {nombre}" }).ClickAsync();
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Confirmar revocación" }).ClickAsync();
        await Expect(fila).ToContainTextAsync("Revocada");
        await Expect(fila.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);

        Assert.Equal(HttpStatusCode.Unauthorized, await ConsultarConClaveAsync(clave));
    }

    [Fact]
    public async Task Crea_una_clave_del_ambiente_de_pruebas()
    {
        var pagina = await portal.IniciarSesionAsync();
        var nombre = $"Pruebas {Random.Shared.Next(1000, 9999)}";

        await pagina.GotoAsync("/integracion");
        await pagina.GetByLabel("Nombre", new() { Exact = true }).FillAsync(nombre);
        await pagina.GetByLabel("Ambiente", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Pruebas" });
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear clave" }).ClickAsync();

        var campo = pagina.GetByLabel("Clave de API", new() { Exact = true });
        await Expect(campo).ToHaveValueAsync(new Regex("^lmuy_test_\\S{40,}$"));
        var clave = await campo.InputValueAsync();

        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre });
        await Expect(fila).ToContainTextAsync("Pruebas");
        await Expect(fila).ToContainTextAsync("Activa");

        Assert.Equal(HttpStatusCode.OK, await ConsultarConClaveAsync(clave));
    }

    [Fact]
    public async Task La_documentacion_de_la_api_publica_se_ve_en_el_navegador()
    {
        var contexto = await portal.Navegador.NewContextAsync(new BrowserNewContextOptions { Locale = "es-UY" });
        var pagina = await contexto.NewPageAsync();

        await pagina.GotoAsync(portal.DireccionApi + "/api/v1/docs/");

        await Expect(pagina.GetByText("LastMileUY · API pública").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(pagina.GetByText("Importa hasta 100 envíos de una vez, sin duplicar").First).ToBeVisibleAsync();
        await Expect(pagina.GetByText("X-Api-Key").First).ToBeVisibleAsync();

        await contexto.CloseAsync();
    }

    private async Task<HttpStatusCode> ConsultarConClaveAsync(string clave)
    {
        using var sistema = new HttpClient { BaseAddress = new Uri(portal.DireccionApi + "/") };
        sistema.DefaultRequestHeaders.Add("X-Api-Key", clave);
        using var respuesta = await sistema.GetAsync("api/v1/envios");
        return respuesta.StatusCode;
    }
}
