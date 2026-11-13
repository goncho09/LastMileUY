namespace LastMileUY.E2E.Tests;

using System.Text.RegularExpressions;
using LastMileUY.Contracts.Mensajeria;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

[Collection(ColeccionPortalComercioEnNavegador.Nombre)]
public class PortalAvisosTests(PortalComercioEnNavegador portal)
{
    private const string OperadorPrueba1 = "Operador Prueba 1";

    [Fact]
    public async Task Crea_una_suscripcion_ve_la_clave_una_sola_vez_la_desactiva_y_regenera_la_clave()
    {
        var pagina = await portal.IniciarSesionAsync();
        var url = $"https://tienda{Random.Shared.Next(1000, 9999)}.uy/avisos";

        await pagina.GotoAsync("/avisos");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Suscripciones", Exact = true }).ClickAsync();

        var evento = pagina.GetByLabel("Evento", new() { Exact = true });
        await Expect(evento.Locator("option", new() { HasText = "Cambio de estado de un bulto" })).ToHaveCountAsync(0);
        await pagina.GetByLabel("Versión", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "v2 · con eventos de bulto" });
        await evento.SelectOptionAsync(new SelectOptionValue { Label = "Cambio de estado de un bulto" });

        var campoUrl = pagina.GetByLabel("URL", new() { Exact = true });
        await campoUrl.FillAsync("http://tienda.uy/avisos");
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Agregar" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("La URL tiene que ser https.");

        await campoUrl.FillAsync(url);
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Agregar" }).ClickAsync();

        var panelClave = pagina.GetByRole(AriaRole.Heading, new() { Name = "Clave HMAC disponible" });
        await Expect(panelClave).ToBeVisibleAsync();
        var clave = pagina.GetByLabel("Clave secreta HMAC");
        await Expect(clave).ToHaveValueAsync(new Regex(@"^\S{32,}$"));
        var primeraClave = await clave.InputValueAsync();

        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = url });
        await Expect(fila).ToContainTextAsync("Cambio de estado de un bulto");
        await Expect(fila).ToContainTextAsync("v2");
        await Expect(fila).ToContainTextAsync("Activa");

        await pagina.Locator("section", new() { Has = panelClave }).GetByRole(AriaRole.Button, new() { Name = "Cerrar" }).ClickAsync();
        await Expect(panelClave).ToBeHiddenAsync();

        await fila.GetByRole(AriaRole.Button, new() { Name = "Desactivar", Exact = true }).ClickAsync();
        await Expect(fila).ToContainTextAsync("Inactiva");
        await Expect(fila.GetByRole(AriaRole.Button, new() { Name = "Activar", Exact = true })).ToBeVisibleAsync();

        await fila.GetByRole(AriaRole.Button, new() { Name = "Regenerar clave" }).ClickAsync();
        var confirmacion = pagina.GetByRole(AriaRole.Heading, new() { Name = "¿Regenerar clave HMAC?" });
        await Expect(confirmacion).ToBeVisibleAsync();
        await pagina.Locator("section", new() { Has = confirmacion }).GetByRole(AriaRole.Button, new() { Name = "Cancelar" }).ClickAsync();
        await Expect(confirmacion).ToBeHiddenAsync();
        await Expect(panelClave).ToBeHiddenAsync();

        await fila.GetByRole(AriaRole.Button, new() { Name = "Regenerar clave" }).ClickAsync();
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Confirmar regeneración" }).ClickAsync();
        await Expect(panelClave).ToBeVisibleAsync();
        await Expect(clave).ToHaveValueAsync(new Regex(@"^\S{32,}$"));
        Assert.NotEqual(primeraClave, await clave.InputValueAsync());

        await pagina.ReloadAsync();
        await pagina.GetByRole(AriaRole.Button, new() { Name = "Suscripciones", Exact = true }).ClickAsync();
        await Expect(fila).ToContainTextAsync("Inactiva");
        await Expect(panelClave).ToBeHiddenAsync();
    }

    [Fact]
    public async Task En_el_historial_ve_los_intentos_de_un_aviso_que_no_llego_y_lo_reenvia()
    {
        var envio = await portal.EnvioDelComercioAsync();
        await AvisoFallidoAsync(envio.Id, suscripcionActiva: true);

        var envioDesactivada = await portal.EnvioDelComercioAsync();
        await AvisoFallidoAsync(envioDesactivada.Id, suscripcionActiva: false);

        var pagina = await portal.IniciarSesionAsync(OperadorPrueba1);
        await pagina.GotoAsync("/avisos");
        await pagina.GetByLabel("Estado", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "No recibidos" });

        var fila = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = envio.CodigoSeguimiento });
        await Expect(fila).ToContainTextAsync("No recibido");
        await Expect(fila.GetByRole(AriaRole.Cell).Nth(3)).ToHaveTextAsync("2");

        await fila.GetByRole(AriaRole.Button, new() { Name = "Ver intentos" }).ClickAsync();
        await Expect(pagina.GetByText("Destino: https://tienda.uy/avisos/historial")).ToBeVisibleAsync();
        await Expect(pagina.GetByText("Respondió con error 503")).ToBeVisibleAsync();
        await Expect(pagina.GetByText("Sin respuesta")).ToBeVisibleAsync();
        await fila.GetByRole(AriaRole.Button, new() { Name = "Ocultar intentos" }).ClickAsync();
        await Expect(pagina.GetByText("Respondió con error 503")).ToBeHiddenAsync();

        var filaDesactivada = pagina.GetByRole(AriaRole.Row).Filter(new() { HasText = envioDesactivada.CodigoSeguimiento });
        await filaDesactivada.GetByRole(AriaRole.Button, new() { Name = "Reenviar" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Alert)).ToContainTextAsync("La suscripción del aviso está desactivada");
        await Expect(filaDesactivada).ToContainTextAsync("No recibido");

        await fila.GetByRole(AriaRole.Button, new() { Name = "Reenviar" }).ClickAsync();
        await Expect(pagina.GetByRole(AriaRole.Status))
            .ToContainTextAsync($"Aviso del envío {envio.CodigoSeguimiento} reenviado");
        await Expect(fila).ToHaveCountAsync(0);

        await pagina.GetByLabel("Estado", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Enviando" });
        await Expect(fila).ToContainTextAsync("Enviando…");
        await Expect(fila.GetByRole(AriaRole.Button, new() { Name = "Reenviar" })).ToHaveCountAsync(0);
    }

    private Task<object[]> AvisoFallidoAsync(int envioId, bool suscripcionActiva) => portal.ConsultarAsync($"""
        WITH suscripcion AS (
            INSERT INTO "SuscripcionesAvisos"("OperadorId", "ComercioId", "TipoEvento", "UrlDestino", "ClaveFirma",
                                              "Activa", "VersionContrato", "FechaCreacion")
            SELECT "OperadorId", "ComercioId", '{TiposEvento.EnvioEstadoCambiado}', 'https://tienda.uy/avisos/historial',
                   'clave-de-prueba', {suscripcionActiva}, 1, now()
            FROM "Envios" WHERE "Id" = {envioId}
            RETURNING "Id"),
        aviso AS (
            INSERT INTO "EntregasAvisos"("SuscripcionAvisosId", "EnvioId", "EventoId", "TipoEvento", "Contenido",
                                         "Estado", "CantidadIntentos", "FechaCreacion", "FechaUltimoIntento", "UltimoError")
            SELECT "Id", {envioId}, gen_random_uuid(), '{TiposEvento.EnvioEstadoCambiado}', '{"{"}{"}"}', 3, 2,
                   now() - interval '5 minutes', now() - interval '1 minute', 'Sin respuesta'
            FROM suscripcion
            RETURNING "Id")
        INSERT INTO "IntentosAvisos"("EntregaAvisoId", "NumeroIntento", "Fecha", "Exitoso", "CodigoHttp", "Error", "DuracionMs")
        SELECT "Id", 1, now() - interval '4 minutes', false, 503, 'Respondió 503', 120 FROM aviso
        UNION ALL
        SELECT "Id", 2, now() - interval '1 minute', false, NULL, 'Tiempo agotado', 10000 FROM aviso
        RETURNING "EntregaAvisoId"
        """);
}
