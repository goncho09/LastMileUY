namespace LastMileUY.E2E.Tests;

using Microsoft.Playwright;
using static AppRepartidorEnNavegador;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionAppRepartidorEnNavegador.Nombre)]
public class AppRepartidorSinConexionTests(AppRepartidorEnNavegador app)
{
    [Fact]
    public async Task Sin_senal_abre_la_app_registra_la_entrega_y_la_manda_sola_al_volver_la_conexion()
    {
        var (rutaId, envioId, codigoBulto) = await app.RutaDespachadaAsync(diasAdelante: 2);
        var (celular, pagina) = await app.AbrirCelularAsync();

        await pagina.GotoAsync("/login");
        await pagina.GetByLabel("Correo electrónico").FillAsync(Repartidor);
        await pagina.GetByLabel("Contraseña", new() { Exact = true }).FillAsync(Clave);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Iniciar sesión" }).ClickAsync();
        await pagina.WaitForURLAsync(url => !url.EndsWith("/login"));

        await pagina.GotoAsync($"/rutas/{rutaId}");
        await Expect(pagina.GetByText($"Ruta del navegador +2")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await pagina.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null",
            null, new() { Timeout = 60_000 });

        await celular.SetOfflineAsync(true);
        await pagina.ReloadAsync();
        await Expect(pagina.GetByText($"Ruta del navegador +2")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await pagina.GetByPlaceholder("Código del bulto").FillAsync(codigoBulto);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await Expect(pagina.GetByText("1 / 1")).ToBeVisibleAsync();

        await pagina.GetByRole(AriaRole.Button, new() { Name = "Confirmar entrega" }).ClickAsync();
        await pagina.GetByLabel("Quién recibe").FillAsync("Ana Silva");
        await FirmarAsync(pagina);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Confirmar entrega" }).Last.ClickAsync();

        var pendiente = pagina.GetByText("Operación pendiente de sincronización");
        await Expect(pendiente).ToBeVisibleAsync();

        Assert.Equal("EnTransito", (await app.EnvioAsync(envioId)).Estado);

        await pagina.ReloadAsync();
        await Expect(pendiente).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await celular.SetOfflineAsync(false);

        var envio = await EsperarAsync(() => app.EnvioAsync(envioId), e => e.Estado == "Entregado");
        Assert.Equal("Entregado", envio.Estado);
        var intento = Assert.Single(envio.Intentos);
        Assert.Equal("Entregado", intento.Resultado);
        Assert.True(intento.Prueba!.TieneFirma);
        Assert.Equal("Ana Silva", intento.Prueba.NombreReceptor);

        await pagina.ReloadAsync();
        await Expect(pagina.GetByText("Entrega registrada correctamente")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(pendiente).ToHaveCountAsync(0);

        await celular.CloseAsync();
    }

    private static async Task FirmarAsync(IPage pagina)
    {
        var lienzo = pagina.Locator("canvas[aria-label='Firma de quien recibe']");
        await lienzo.ScrollIntoViewIfNeededAsync();
        var caja = (await lienzo.BoundingBoxAsync())!;

        await pagina.Mouse.MoveAsync(caja.X + 20, caja.Y + caja.Height / 2);
        await pagina.Mouse.DownAsync();
        await pagina.Mouse.MoveAsync(caja.X + caja.Width / 2, caja.Y + 20, new() { Steps = 8 });
        await pagina.Mouse.MoveAsync(caja.X + caja.Width - 20, caja.Y + caja.Height - 20, new() { Steps = 8 });
        await pagina.Mouse.UpAsync();
    }

    private static async Task<T> EsperarAsync<T>(Func<Task<T>> consultar, Func<T, bool> condicion)
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        T valor;

        do
        {
            valor = await consultar();

            if (condicion(valor))
                return valor;

            await Task.Delay(500);
        }
        while (DateTime.UtcNow < limite);

        return valor;
    }
}
