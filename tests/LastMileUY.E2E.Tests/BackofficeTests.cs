namespace LastMileUY.E2E.Tests;

using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static BackofficeEnNavegador;
using static Microsoft.Playwright.Assertions;

// El Backoffice usado desde un navegador real, como lo usa el personal del operador. Cubre lo que las
// pruebas de las páginas (LastMileUY.Backoffice.Tests) no ven: lo que pasa en el navegador entre un
// pedido y otro (formularios, botones, redirecciones, cookies) y cómo se ve en un celular.
// Los datos de prueba están en memoria y los comparten todas las pruebas: solo una arma una ruta.
[Collection(ColeccionBackofficeEnNavegador.Nombre)]
public class BackofficeTests(BackofficeEnNavegador backoffice)
{
    [Fact]
    public async Task Sin_sesion_manda_a_iniciar_sesion_y_al_cerrarla_vuelve_ahi()
    {
        var contexto = await backoffice.Navegador.NewContextAsync(new() { BaseURL = backoffice.Direccion });
        var anonima = await Esperas.Preparar(contexto).NewPageAsync();

        await anonima.GotoAsync("/Envios");
        await Expect(anonima).ToHaveURLAsync(new Regex("/Login", RegexOptions.IgnoreCase));

        var pagina = await backoffice.IniciarSesionAsync(Operador);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Cerrar sesión" }).ClickAsync();
        await Expect(pagina).ToHaveURLAsync(new Regex("/Login", RegexOptions.IgnoreCase));

        // Sin la cookie, lo de antes ya no se ve
        await pagina.GotoAsync("/Envios");
        await Expect(pagina).ToHaveURLAsync(new Regex("/Login", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task El_despachador_no_ve_la_configuracion_del_operador()
    {
        var pagina = await backoffice.IniciarSesionAsync(Despachador);

        await Expect(pagina.GetByRole(AriaRole.Link, new() { Name = "Hojas de ruta" })).ToBeVisibleAsync();
        await Expect(pagina.GetByRole(AriaRole.Link, new() { Name = "Mi operador" })).ToHaveCountAsync(0);

        // Aunque escriba la dirección a mano
        await pagina.GotoAsync("/Operador");
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "No tenés permiso para ver esta página" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Filtrar_los_envios_y_abrir_el_detalle_con_los_intentos()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        await pagina.GotoAsync("/Envios");

        await pagina.GetByLabel("Estado").SelectOptionAsync("Entregado");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Filtrar" }).ClickAsync();

        await Expect(pagina).ToHaveURLAsync(new Regex("estado=Entregado"));
        var filas = pagina.Locator("tbody tr");
        Assert.True(await filas.CountAsync() > 0);
        foreach (var fila in await filas.AllAsync())
            await Expect(fila).ToContainTextAsync("Entregado");

        await filas.First.GetByRole(AriaRole.Link, new() { Name = "Ver detalle" }).ClickAsync();
        await Expect(pagina).ToHaveURLAsync(new Regex("/Envios/Detalle/", RegexOptions.IgnoreCase));
        await Expect(pagina.GetByRole(AriaRole.Heading, new() { Name = "Intentos de entrega" })).ToBeVisibleAsync();
    }

    // Todo el armado de una ruta: ordenar por recorrido, armarla con ese orden y despacharla.
    // También cuida que después de ordenar, "Armar hoja de ruta" arme la ruta y no vuelva a ordenar
    // (la dirección de la página queda con ?handler=ordenar)
    [Fact]
    public async Task Armar_una_ruta_con_el_orden_sugerido_y_despacharla()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        await pagina.GotoAsync("/Rutas/Nueva");

        var casillas = pagina.Locator("input[name='Seleccionados']");
        foreach (var casilla in await casillas.AllAsync())
            await casilla.CheckAsync();

        await pagina.GetByRole(AriaRole.Button, new() { Name = "Ordenar por recorrido" }).ClickAsync();

        var aviso = pagina.GetByRole(AriaRole.Status);
        await Expect(aviso).ToContainTextAsync("Orden sugerido");
        await Expect(aviso).ToContainTextAsync("Sin ubicación, van al final: R4T8Y2U6P9HZ");

        // Los números quedan cargados del 1 al 5, y el que no tiene ubicación es el último
        var numeros = await pagina.Locator("input[name^='Orden']").EvaluateAllAsync<string[]>("e => e.map(x => x.value)");
        Assert.Equal(["1", "2", "3", "4", "5"], numeros.Where(n => n != "").ToArray());
        await Expect(pagina.Locator("tbody tr").Last).ToContainTextAsync("R4T8Y2U6P9HZ");
        var codigos = await pagina.Locator("tbody tr label").AllInnerTextsAsync();

        await pagina.GetByLabel("Nombre").FillAsync("Centro desde el navegador");
        await pagina.GetByLabel("Repartidor").SelectOptionAsync(new SelectOptionValue { Label = "Lucía Méndez" });
        await pagina.GetByLabel("Vehículo").SelectOptionAsync(new SelectOptionValue { Index = 2 });
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Armar hoja de ruta" }).ClickAsync();

        await Expect(pagina).ToHaveURLAsync(new Regex("/Rutas/Detalle/", RegexOptions.IgnoreCase));
        await Expect(pagina.GetByRole(AriaRole.Status)).ToContainTextAsync("con 5 paradas");

        // Las paradas salen en el orden sugerido
        var paradas = await pagina.Locator("tbody tr").AllInnerTextsAsync();
        Assert.Equal(codigos.Select(c => c.Trim()), paradas.Select(p => codigos.First(p.Contains).Trim()));

        await pagina.GetByRole(AriaRole.Button, new() { Name = "Despachar ruta" }).ClickAsync();
        await Expect(pagina.GetByText("Despachada").First).ToBeVisibleAsync();
        await Expect(pagina.GetByRole(AriaRole.Button, new() { Name = "Despachar ruta" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Guardar_el_deposito_con_coma_decimal_y_rechazar_lo_que_no_es_un_numero()
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador);
        await pagina.GotoAsync("/Operador");
        var deposito = pagina.Locator("#deposito");

        await deposito.GetByLabel("Latitud").FillAsync("-34,8712");
        await deposito.GetByLabel("Longitud").FillAsync("-56.1654");
        await deposito.GetByRole(AriaRole.Button, new() { Name = "Guardar depósito" }).ClickAsync();

        await Expect(deposito.GetByRole(AriaRole.Status)).ToContainTextAsync("Se guardó el depósito");
        await Expect(deposito.GetByLabel("Latitud")).ToHaveValueAsync("-34.8712");
        await Expect(deposito.GetByRole(AriaRole.Link, new() { Name = "Ver en el mapa" }))
            .ToHaveAttributeAsync("href", new Regex("mlat=-34.8712&mlon=-56.1654"));

        await deposito.GetByLabel("Latitud").FillAsync("34°54'");
        await deposito.GetByRole(AriaRole.Button, new() { Name = "Guardar depósito" }).ClickAsync();

        await Expect(deposito.GetByRole(AriaRole.Alert)).ToContainTextAsync("grados decimales");
        await Expect(deposito.GetByLabel("Latitud")).ToHaveValueAsync("-34.8712");
    }

    // En un celular (375 px de ancho) ninguna pantalla se tiene que poder correr de costado: las
    // tablas anchas se desplazan dentro de su recuadro, no la página entera
    [Theory]
    [InlineData("/")]
    [InlineData("/Envios")]
    [InlineData("/Rutas")]
    [InlineData("/Rutas/Nueva")]
    [InlineData("/Operador")]
    public async Task En_un_celular_la_pagina_no_se_corre_de_costado(string ruta)
    {
        var pagina = await backoffice.IniciarSesionAsync(Operador, ancho: 375, alto: 812);
        await pagina.GotoAsync(ruta);

        var ancho = await pagina.EvaluateAsync<int>("document.documentElement.scrollWidth");

        Assert.True(ancho <= 375, $"{ruta}: la página mide {ancho} px de ancho en una pantalla de 375 px");
    }
}
