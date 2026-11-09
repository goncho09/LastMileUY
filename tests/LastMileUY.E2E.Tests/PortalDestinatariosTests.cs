namespace LastMileUY.E2E.Tests;

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

// El comercio carga destinatarios desde su portal, como lo haría una persona, y les manda envíos
[Collection(ColeccionPortalComercioEnNavegador.Nombre)]
public class PortalDestinatariosTests(PortalComercioEnNavegador portal)
{
    [Fact]
    public async Task Desde_el_alta_de_envio_carga_un_destinatario_con_ubicacion_y_le_crea_el_envio()
    {
        var pagina = await portal.IniciarSesionAsync();
        var apellido = "Navegador" + Random.Shared.Next(1000, 9999);

        // Desde el formulario de envíos: "¿No está en la lista?"
        await pagina.GotoAsync("/envios/nuevo");
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Cargá un destinatario nuevo" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Nuevo destinatario" })).ToBeVisibleAsync();

        await pagina.GetByLabel("Nombre").FillAsync("Lucía");
        await pagina.GetByLabel("Apellido").FillAsync(apellido);
        await pagina.GetByLabel("Email").FillAsync("lucia@correo.uy");
        await pagina.GetByLabel("Calle").FillAsync("Av. Artigas");
        await pagina.GetByLabel("Número").FillAsync("456");
        await pagina.GetByLabel("Ciudad o localidad").FillAsync("San José de Mayo");
        await pagina.GetByLabel("Departamento").SelectOptionAsync("San José");

        // Una ubicación mal escrita se avisa antes de mandar
        var ubicacion = pagina.GetByLabel("Ubicación en el mapa");
        await ubicacion.FillAsync("cerca de la plaza");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Guardar destinatario" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("latitud y longitud");

        // Como la copia Google Maps
        await ubicacion.FillAsync("-34.3375, -56.7136");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Guardar destinatario" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync($"Se guardó a Lucía {apellido}");
        var tarjeta = pagina.GetByRole(AriaRole.Listitem).Filter(new() { HasText = $"{apellido}, Lucía" });
        await Expect(tarjeta).ToContainTextAsync("Av. Artigas 456, San José de Mayo (San José)");
        await Expect(tarjeta).ToContainTextAsync("Con ubicación");

        // Vuelve al envío y el destinatario nuevo ya está para elegir
        await pagina.GetByRole(AriaRole.Link, new() { Name = "Crear el envío" }).ClickAsync();
        await pagina.WaitForURLAsync("**/envios/nuevo");
        await pagina.GetByLabel("Destinatario").SelectOptionAsync(new SelectOptionValue { Label = $"{apellido}, Lucía" });
        await Expect(pagina.GetByLabel("Dirección de entrega")).ToHaveValueAsync(new Regex(@"^\d+$"));

        await pagina.GetByLabel("Peso (kg)").FillAsync("2");
        await pagina.GetByLabel("Largo (cm)").FillAsync("30");
        await pagina.GetByLabel("Ancho (cm)").FillAsync("20");
        await pagina.GetByLabel("Alto (cm)").FillAsync("10");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear envío" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "¡Envío creado!" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Le_agrega_otra_direccion_a_un_destinatario()
    {
        var pagina = await portal.IniciarSesionAsync();

        await pagina.GotoAsync("/destinatarios");
        var primero = pagina.Locator("ul > li").First;
        await Expect(primero.GetByRole(AriaRole.Button, new() { Name = "+ Agregar dirección" })).ToBeVisibleAsync();
        var direccionesAntes = await primero.Locator("ul > li").CountAsync();

        await primero.GetByRole(AriaRole.Button, new() { Name = "+ Agregar dirección" }).ClickAsync();
        await primero.GetByLabel("Calle").FillAsync("Rambla Gandhi");
        await primero.GetByLabel("Número").FillAsync("100");
        await primero.GetByLabel("Ciudad o localidad").FillAsync("Montevideo");
        await primero.GetByRole(AriaRole.Button, new() { Name = "Guardar dirección" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Se agregó la dirección");
        await Expect(primero.Locator("ul > li")).ToHaveCountAsync(direccionesAntes + 1);
        await Expect(primero).ToContainTextAsync("Rambla Gandhi 100");
        await Expect(primero).ToContainTextAsync("Sin ubicación");
    }
}
