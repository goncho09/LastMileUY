namespace LastMileUY.E2E.Tests;

using Microsoft.Playwright;
using static BackofficeEnNavegador;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionBackofficeEnNavegador.Nombre)]
public class BackofficeAdministracionTests(BackofficeEnNavegador backoffice)
{
    [Fact]
    public async Task Crea_un_operador_con_su_usuario_inicial_y_queda_en_el_listado()
    {
        var pagina = await backoffice.IniciarSesionAsync(Administrador);
        var nombre = $"Envíos {Random.Shared.Next(1000, 9999)}";
        var email = $"responsable{Random.Shared.Next(1000, 9999)}@prueba.test";

        await pagina.GotoAsync("/Operadores/Crear");

        var campoNombre = pagina.GetByLabel("Nombre del operador");
        Assert.NotEqual("0px", await campoNombre.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));

        await campoNombre.FillAsync(nombre);
        await pagina.GetByLabel("Color primario").FillAsync("#0EA5E9");
        await CompletarUsuarioInicialAsync(pagina, email);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear operador" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Operador creado correctamente.");
        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre })).ToBeVisibleAsync();

        await pagina.ReloadAsync();
        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre })).ToBeVisibleAsync();

        await pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre }).GetByRole(AriaRole.Link, new() { Name = "Editar" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Main).GetByRole(AriaRole.Heading, new() { Name = "Editar operador", Level = 1 })).ToBeVisibleAsync();
        await Expect(pagina.GetByLabel("Nombre del operador")).ToHaveValueAsync(nombre);
        await pagina.GetByLabel("Nombre del operador").FillAsync(nombre + " editado");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Guardar cambios" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Operador actualizado correctamente.");
        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre + " editado" })).ToBeVisibleAsync();

        await pagina.GotoAsync("/Operadores/Crear");
        await pagina.GetByLabel("Nombre del operador").FillAsync(nombre + " bis");
        await CompletarUsuarioInicialAsync(pagina, email);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear operador" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("Ya existe un usuario con ese correo.");
    }

    [Fact]
    public async Task Crea_un_comercio_y_lo_vincula_con_un_operador()
    {
        var pagina = await backoffice.IniciarSesionAsync(Administrador);
        var nombre = $"Tienda {Random.Shared.Next(1000, 9999)}";

        await pagina.GotoAsync("/Comercios/Crear");
        await pagina.GetByLabel("Nombre del comercio").FillAsync(nombre);
        await CompletarUsuarioInicialAsync(pagina, $"tienda{Random.Shared.Next(1000, 9999)}@prueba.test");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear comercio" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("creado");
        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = nombre });
        await Expect(fila).ToBeVisibleAsync();

        await fila.GetByRole(AriaRole.Link, new() { Name = "Operadores" }).ClickAsync();
        await pagina.GetByLabel("Operador disponible").SelectOptionAsync(new SelectOptionValue { Label = "Envíos del Sur" });
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Vincular" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = "Envíos del Sur" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Sin_permiso_el_administrador_vuelve_a_su_inicio()
    {
        var pagina = await backoffice.IniciarSesionAsync(Administrador);

        await pagina.GotoAsync("/Operador");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "No tenés permiso para ver esta página" })).ToBeVisibleAsync();

        await pagina.GetByRole(AriaRole.Link, new() { Name = "Ir al inicio" }).ClickAsync();
        await pagina.WaitForURLAsync(url => url.Contains("mensajesfallidos", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task CompletarUsuarioInicialAsync(IPage pagina, string email)
    {
        await pagina.GetByLabel("Nombre", new() { Exact = true }).FillAsync("Ana");
        await pagina.GetByLabel("Apellido", new() { Exact = true }).FillAsync("Gómez");
        await pagina.GetByLabel("Email para iniciar sesión").FillAsync(email);
        await pagina.GetByLabel("Contraseña inicial").FillAsync("Prueba1234");
    }
}
