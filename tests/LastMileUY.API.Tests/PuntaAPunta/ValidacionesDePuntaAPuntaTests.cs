namespace LastMileUY.API.Tests.PuntaAPunta;

using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Avisos;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Sincronizacion;
using static ApiDePuntaAPunta;

// Pedidos fuera de los límites: la API los contesta con un 400 (o rechaza la operación) en vez
// de dejar que la base los corte con un 500. Y los datos de contacto de los destinatarios solo
// los ve quien da de alta envíos.
[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ValidacionesDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private static readonly string Png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);

    [Theory]
    [InlineData(Repartidor1)]
    [InlineData(Despachador1)]
    public async Task Los_destinatarios_no_se_le_muestran_a_quien_no_da_de_alta_envios(string email)
    {
        var cliente = await api.IniciarSesionAsync(email);

        using var respuesta = await cliente.GetAsync("api/destinatarios");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Theory]
    [InlineData(Operador1)]
    [InlineData(Comercio1)]
    public async Task Los_destinatarios_si_se_le_muestran_a_quien_da_de_alta_envios(string email)
    {
        var cliente = await api.IniciarSesionAsync(email);

        using var respuesta = await cliente.GetAsync("api/destinatarios");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_referencia_externa_demasiado_larga_es_un_400()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);

        using var respuesta = await comercio.PostAsJsonAsync("api/envios", new CrearEnvioRequest
        {
            ComercioId = api.IdComercio1,
            DestinatarioId = destinatario,
            DireccionId = direccion,
            ReferenciaExterna = new string('R', 101)
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Una_referencia_externa_en_blanco_no_choca_con_otra_en_blanco()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);
        var (destinatario, direccion) = await api.DestinatarioDeAsync(api.IdComercio1);

        // Un formulario que manda el campo vacío no tiene referencia: no es un duplicado
        for (var i = 0; i < 2; i++)
        {
            using var respuesta = await comercio.PostAsJsonAsync("api/envios", new CrearEnvioRequest
            {
                ComercioId = api.IdComercio1,
                DestinatarioId = destinatario,
                DireccionId = direccion,
                ReferenciaExterna = "  "
            });

            Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        }
    }

    [Fact]
    public async Task Una_pagina_enorme_devuelve_una_pagina_vacia()
    {
        var operador = await api.IniciarSesionAsync(Operador1);

        using var respuesta = await operador.GetAsync($"api/envios?pagina={int.MaxValue}&tamanioPagina=100");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty((await respuesta.Content.ReadFromJsonAsync<Contracts.Comun.PaginaDto<EnvioResumenDto>>())!.Items);
    }

    // También en las respuestas de error (acá, un 401 sin sesión)
    [Fact]
    public async Task Toda_respuesta_le_dice_al_navegador_que_no_adivine_el_tipo_de_contenido()
    {
        using var respuesta = await api.ClienteSinSesion().GetAsync("api/envios");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("nosniff", Assert.Single(respuesta.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task Una_url_de_aviso_demasiado_larga_es_un_400()
    {
        var comercio = await api.IniciarSesionAsync(Comercio1);

        using var respuesta = await comercio.PostAsJsonAsync("api/avisos/suscripciones", new CrearSuscripcionAvisosRequest
        {
            TipoEvento = Contracts.Mensajeria.TiposEvento.EnvioAdmitido,
            UrlDestino = "https://comercio.example/" + new string('a', 2000)
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    // Si la app manda un texto más largo de lo que entra en la base, reintentar no lo arregla:
    // se rechaza (y la app deja de reenviarla) en vez de contestar "Error, volvé a sincronizar" para siempre.
    [Fact]
    public async Task Una_entrega_con_textos_demasiado_largos_se_rechaza_y_no_se_reintenta()
    {
        var operador = await api.IniciarSesionAsync(Operador1);
        var despachador = await api.IniciarSesionAsync(Despachador1);
        var repartidor = await api.IniciarSesionAsync(Repartidor1);
        var (rutaId, envioId) = await RutasDePuntaAPunta.RutaDespachadaAsync(api, operador, despachador, diasAdelante: 20);

        var resultado = await SincronizacionDePuntaAPuntaTests.SincronizarAsync(repartidor, new OperacionRequest
        {
            IdOperacion = Guid.NewGuid(),
            Tipo = "Entrega",
            RutaId = rutaId,
            EnvioId = envioId,
            FechaHoraDispositivo = DateTime.UtcNow.AddMinutes(-5),
            Latitud = -34.9m,
            Longitud = -56.16m,
            NombreReceptor = new string('N', 151),
            FirmaBase64 = Png
        });

        Assert.Equal("Rechazada", resultado.Resultado);
        Assert.False(resultado.Reintentar);
        Assert.Equal("EnTransito", resultado.EstadoEnvio);
    }
}
