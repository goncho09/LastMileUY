namespace LastMileUY.E2E.Tests;

using Microsoft.Playwright;
using static BackofficeEnNavegador;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionBackofficeEnNavegador.Nombre)]
public class BackofficeConfiguracionTests(BackofficeEnNavegador backoffice)
{
    [Fact]
    public async Task Agrega_un_vehiculo_con_decimales_y_rechaza_la_matricula_repetida()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        var matricula = $"AAA{Random.Shared.Next(1000, 9999)}";

        await pagina.GotoAsync("/Configuracion/Vehiculos");
        await pagina.GetByLabel("Matrícula").FillAsync(matricula.ToLowerInvariant());
        await pagina.GetByLabel("Tipo").FillAsync("Camioneta");
        await pagina.GetByLabel("Peso máx. (kg)").FillAsync("1200.5");
        await pagina.GetByLabel("Volumen máx. (m³)").FillAsync("6.25");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Agregar vehículo" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync($"Se agregó el vehículo {matricula}.");
        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = matricula })).ToContainTextAsync("1200,5 kg · 6,25 m³");

        await pagina.GetByLabel("Matrícula").FillAsync(matricula);
        await pagina.GetByLabel("Tipo").FillAsync("Moto");
        await pagina.GetByLabel("Peso máx. (kg)").FillAsync("20");
        await pagina.GetByLabel("Volumen máx. (m³)").FillAsync("0.1");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Agregar vehículo" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync($"Ya hay un vehículo con la matrícula {matricula}.");
    }

    [Fact]
    public async Task Crea_una_zona_y_le_agrega_una_franja_horaria()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        var zona = $"Zona {Random.Shared.Next(1000, 9999)}";

        await pagina.GotoAsync("/Configuracion/Zonas");
        await pagina.GetByLabel("Nombre").FillAsync(zona);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear zona" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync($"Se creó la zona \"{zona}\".");

        await pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = zona }).GetByRole(AriaRole.Link, new() { Name = "0 activas" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = $"Franjas de {zona}" })).ToBeVisibleAsync();

        await pagina.GetByLabel("Nombre").FillAsync("Noche");
        await pagina.GetByLabel("Desde").FillAsync("20:00");
        await pagina.GetByLabel("Hasta").FillAsync("19:00");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear franja" }).ClickAsync();
        await Expect(pagina.GetByText("La hora de fin tiene que ser posterior a la de inicio.")).ToBeVisibleAsync();

        await pagina.GetByLabel("Hasta").FillAsync("22:30");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear franja" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Se creó la franja \"Noche\".");
        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = "Noche" })).ToContainTextAsync("20:00 a 22:30");
    }

    [Fact]
    public async Task Una_version_nueva_del_cuadro_se_edita_y_la_vigente_queda_de_solo_lectura()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        var desde = DateTime.Today.AddDays(Random.Shared.Next(30, 3000));

        await pagina.GotoAsync("/Configuracion/Tarifas");
        await pagina.GetByLabel("Rige desde").FillAsync(desde.ToString("yyyy-MM-dd"));
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Crear versión" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("Se creó la versión nueva.");
        await pagina.GetByLabel("Nombre").FillAsync("Más de 20 kg");
        await pagina.GetByLabel("Peso desde (kg)").FillAsync("20");
        await pagina.GetByLabel("Peso hasta (kg)").FillAsync("50");
        await pagina.GetByLabel("Precio base ($)").FillAsync("650.75");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Agregar tarifa" }).ClickAsync();

        await Expect(pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = "Más de 20 kg" })).ToContainTextAsync("$ 650,75");

        await pagina.GotoAsync("/Configuracion/CuadroTarifario?id=1");
        await Expect(pagina.GetByText("Ya rige o ya venció")).ToBeVisibleAsync();
        await Expect(pagina.GetByRole(AriaRole.Button, new() { Name = "Agregar tarifa" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task El_despachador_no_ve_ni_entra_a_la_configuracion()
    {
        var pagina = await backoffice.IniciarSesionAsync(Despachador);

        await Expect(pagina.GetByRole(AriaRole.Link, new() { Name = "Zonas de cobertura" })).ToHaveCountAsync(0);

        await pagina.GotoAsync("/Configuracion/Vehiculos");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Vehículos", Level = 1 })).ToHaveCountAsync(0);
    }
}
