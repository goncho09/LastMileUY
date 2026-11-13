namespace LastMileUY.E2E.Tests;

using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionPortalComercioEnNavegador.Nombre)]
public class PortalEnviosTests(PortalComercioEnNavegador portal)
{
    private const string OperadorPrueba1 = "Operador Prueba 1";

    [Fact]
    public async Task Busca_un_envio_por_su_referencia_y_abre_el_detalle()
    {
        var referencia = "PED-" + Random.Shared.Next(100000, 999999);
        var envio = await portal.EnvioDelComercioAsync(referencia);

        var pagina = await portal.IniciarSesionAsync(OperadorPrueba1);
        await pagina.GotoAsync("/envios");

        var buscar = pagina.GetByLabel("Buscar", new() { Exact = true });
        await buscar.FillAsync(referencia);
        var filas = pagina.Locator("tbody tr");
        await Expect(filas).ToHaveCountAsync(1);
        await Expect(filas).ToContainTextAsync(envio.CodigoSeguimiento);
        await Expect(filas).ToContainTextAsync($"Ref. {referencia}");
        await Expect(filas).ToContainTextAsync("Admitido");

        await buscar.FillAsync("no-hay-ninguno-asi");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "No hay envíos que coincidan con la búsqueda" }))
            .ToBeVisibleAsync();

        await buscar.FillAsync(referencia);
        await pagina.GetByRole(AriaRole.Link, new() { Name = envio.CodigoSeguimiento }).ClickAsync();
        await pagina.WaitForURLAsync($"**/envios/{envio.Id}");

        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = envio.CodigoSeguimiento, Level = 1 })).ToBeVisibleAsync();
        await Expect(pagina.GetByText(referencia)).ToBeVisibleAsync();
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Bultos (1)" })).ToBeVisibleAsync();
        await Expect(pagina.GetByText("Todavía no hubo intentos de entrega.")).ToBeVisibleAsync();

        await pagina.GetByRole(AriaRole.Link, new() { Name = "Mis envíos" }).First.ClickAsync();
        await pagina.WaitForURLAsync("**/envios");
    }

    [Fact]
    public async Task Filtra_por_entregado_parcialmente_y_lo_muestra_con_su_texto()
    {
        var envio = await portal.EnvioDelComercioAsync();
        await portal.ConsultarAsync($"""UPDATE "Envios" SET "Estado" = 11 WHERE "Id" = {envio.Id} RETURNING "Id" """);

        var pagina = await portal.IniciarSesionAsync(OperadorPrueba1);
        await pagina.GotoAsync("/envios");
        await pagina.GetByLabel("Estado", new() { Exact = true })
            .SelectOptionAsync(new SelectOptionValue { Label = "Entregado parcialmente" });

        var fila = pagina.Locator("tbody tr").Filter(new() { HasText = envio.CodigoSeguimiento });
        await Expect(fila).ToContainTextAsync("Entregado parcialmente");
        await Expect(pagina.Locator("tbody tr").Filter(new() { HasText = "Admitido" })).ToHaveCountAsync(0);

        await fila.GetByRole(AriaRole.Link, new() { Name = envio.CodigoSeguimiento }).ClickAsync();
        await Expect(pagina.GetByText("Entregado parcialmente", new() { Exact = true }).First).ToBeVisibleAsync();
        await Expect(pagina.GetByText("EntregadoParcialmente")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Un_envio_que_no_existe_lo_dice_sin_error()
    {
        var pagina = await portal.IniciarSesionAsync(OperadorPrueba1);

        await pagina.GotoAsync("/envios/999999");

        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "No encontramos ese envío" })).ToBeVisibleAsync();
        await Expect(pagina.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    }
}
