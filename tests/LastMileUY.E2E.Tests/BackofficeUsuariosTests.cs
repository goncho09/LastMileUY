namespace LastMileUY.E2E.Tests;

using System.Text.RegularExpressions;
using LastMileUY.Backoffice.Servicios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using static BackofficeEnNavegador;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionBackofficeEnNavegador.Nombre)]
public class BackofficeUsuariosTests(BackofficeEnNavegador backoffice)
{
    [Fact]
    public void En_modo_demo_cada_servicio_de_la_API_tiene_su_version_en_memoria()
    {
        var servicios = typeof(IUsuariosApi).Assembly.GetTypes()
            .Where(t => t is { IsInterface: true, Namespace: "LastMileUY.Backoffice.Servicios" } && Regex.IsMatch(t.Name, "^I.+Api$"))
            .ToList();

        Assert.Contains(typeof(IUsuariosApi), servicios);

        using var alcance = backoffice.Services.CreateScope();
        var faltan = servicios.Where(t => alcance.ServiceProvider.GetService(t) is null).Select(t => t.Name).ToList();

        Assert.True(faltan.Count == 0, "Sin versión en memoria: " + string.Join(", ", faltan));
    }

    [Fact]
    public async Task El_operador_da_de_alta_a_un_repartidor_lo_busca_y_lo_desactiva_con_confirmacion()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        var email = $"nuevo{Random.Shared.Next(1000, 9999)}@prueba.test";

        await pagina.GetByRole(AriaRole.Link, new() { Name = "Usuarios" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Main).GetByRole(AriaRole.Heading, new() { Name = "Gestión de usuarios", Level = 1 })).ToBeVisibleAsync();

        await pagina.GetByLabel("Nombre", new() { Exact = true }).FillAsync("Diego");
        await pagina.GetByLabel("Apellido", new() { Exact = true }).FillAsync("Núñez");
        await pagina.GetByLabel("Correo electrónico").FillAsync(email);
        await pagina.GetByLabel("Contraseña inicial").FillAsync("Repartidor123");
        await pagina.GetByLabel("Rol", new() { Exact = true }).SelectOptionAsync("Repartidor");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear usuario" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Usuario creado correctamente.");
        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = email });
        await Expect(fila).ToContainTextAsync("Diego Núñez");
        await Expect(fila).ToContainTextAsync("Repartidor");
        await Expect(fila).ToContainTextAsync("Activo");

        await pagina.GetByLabel("Nombre", new() { Exact = true }).FillAsync("Otro");
        await pagina.GetByLabel("Apellido", new() { Exact = true }).FillAsync("Usuario");
        await pagina.GetByLabel("Correo electrónico").FillAsync(email);
        await pagina.GetByLabel("Contraseña inicial").FillAsync("Repartidor123");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear usuario" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("Ya existe un usuario con ese correo.");

        await pagina.GetByLabel("Buscar usuarios").FillAsync("NUNEZ");
        await Expect(fila).ToBeVisibleAsync();
        await Expect(pagina.Locator(".fila-usuario:visible")).ToHaveCountAsync(1);
        await Expect(pagina.GetByText(new Regex(@"^Mostrando 1 de \d+ usuarios$"))).ToBeVisibleAsync();

        await pagina.GetByLabel("Buscar usuarios").FillAsync("nadie-se-llama-asi");
        await Expect(pagina.GetByText("No se encontraron usuarios con esos filtros.")).ToBeVisibleAsync();
        await pagina.GetByLabel("Buscar usuarios").FillAsync(email);

        var dialogo = pagina.GetByRole(AriaRole.Dialog, new() { Name = "¿Desactivar usuario?" });
        await fila.GetByRole(AriaRole.Button, new() { Name = "Desactivar" }).ClickAsync();
        await Expect(dialogo).ToBeVisibleAsync();
        await Expect(dialogo).ToContainTextAsync("Diego Núñez");
        await dialogo.GetByRole(AriaRole.Button, new() { Name = "Cancelar" }).ClickAsync();
        await Expect(dialogo).ToBeHiddenAsync();
        await Expect(fila).ToContainTextAsync("Activo");

        await fila.GetByRole(AriaRole.Button, new() { Name = "Desactivar" }).ClickAsync();
        await Expect(dialogo).ToBeVisibleAsync();
        await pagina.Keyboard.PressAsync("Escape");
        await Expect(dialogo).ToBeHiddenAsync();

        await fila.GetByRole(AriaRole.Button, new() { Name = "Desactivar" }).ClickAsync();
        await dialogo.GetByRole(AriaRole.Button, new() { Name = "Sí, desactivar" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Usuario desactivado correctamente.");
        await Expect(fila).ToContainTextAsync("Inactivo");

        await pagina.GetByLabel("Filtrar por estado").SelectOptionAsync("inactivo");
        await Expect(fila).ToBeVisibleAsync();
        await fila.GetByRole(AriaRole.Button, new() { Name = "Activar" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Usuario activado correctamente.");
        await Expect(dialogo).ToBeHiddenAsync();
        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = email })).ToContainTextAsync("Activo");
    }

    [Fact]
    public async Task El_despachador_no_gestiona_usuarios()
    {
        var pagina = await backoffice.IniciarSesionAsync(Despachador);

        await Expect(pagina.GetByRole(AriaRole.Link, new() { Name = "Hojas de ruta" })).ToBeVisibleAsync();
        await Expect(pagina.GetByRole(AriaRole.Link, new() { Name = "Usuarios" })).ToHaveCountAsync(0);

        await pagina.GotoAsync("/Usuarios");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "No tenés permiso para ver esta página" }))
            .ToBeVisibleAsync();
    }
}
