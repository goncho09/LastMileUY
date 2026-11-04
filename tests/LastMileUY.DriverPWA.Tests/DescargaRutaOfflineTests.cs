
using System.Net;
using LastMileUY.DriverPWA.Services;

namespace LastMileUY.DriverPWA.Tests;

public class DescargaRutaOfflineTests
{
    [Fact]
    public async Task La_ruta_conserva_bultos_y_reglas_sin_conexion()
    {
        var navegador = new NavegadorFalso();
        var almacen = new AlmacenLocal(
            navegador,
            TimeProvider.System);

        await almacen.AsignarDuenoAsync("repartidor-1");

        // Simula la respuesta de la API con los campos nuevos.
        const string json = """
        {
          "id": 10,
          "nombre": "Ruta de prueba",
          "fecha": "2026-11-03",
          "estado": "Despachada",
          "repartidor": {
            "id": 1,
            "nombre": "Repartidor de prueba"
          },
          "vehiculo": {
            "id": 1,
            "matricula": "ABC123",
            "tipo": "Moto",
            "capacidadPesoKg": 50,
            "capacidadVolumenM3": 1
          },
          "pesoTotalKg": 3,
          "volumenTotalM3": 0.1,
          "paradas": [
            {
              "orden": 1,
              "envioId": 100,
              "codigoSeguimiento": "ENV-100",
              "estadoEnvio": "EnTransito",
              "cantidadBultos": 2,
              "pesoKg": 3,
              "volumenM3": 0.1,
              "modalidad": "Estandar",
              "evidencia": {
                "firma": false,
                "foto": false,
                "documentoReceptor": false
              },
              "bultos": [
                {
                  "id": 501,
                  "codigo": "B-501",
                  "estado": "Pendiente",
                  "descripcion": "Primer bulto",
                  "pesoKg": 1
                },
                {
                  "id": 502,
                  "codigo": "B-502",
                  "estado": "Entregado",
                  "descripcion": "Segundo bulto",
                  "pesoKg": 2
                }
              ]
            }
          ],
          "reglaEntregaParcial": {
            "version": 2,
            "permiteEntregaParcial": true,
            "destinoBultosPendientes": "Reprogramacion"
          }
        }
        """;

        var conexion = new ConexionSimulada(json);

        using var http = new HttpClient(conexion)
        {
            BaseAddress = new Uri("https://api.prueba/")
        };

        var rutas = new RutasService(http, almacen);

        // Primera descarga: hay conexión.
        var descargada = await rutas.ObtenerRutaAsync(10);

        Assert.NotNull(descargada);
        Assert.Equal(2, descargada.Paradas[0].Bultos?.Count);
        Assert.Equal(2, descargada.ReglaEntregaParcial?.Version);

        // Simulamos pérdida de conexión.
        conexion.SinConexion = true;

        // La misma ruta debe recuperarse del almacenamiento.
        var recuperada = await rutas.ObtenerRutaAsync(10);

        Assert.NotNull(recuperada);
        Assert.Equal(10, recuperada.Id);

        var bultos = recuperada.Paradas[0].Bultos;

        Assert.NotNull(bultos);
        Assert.Equal(2, bultos.Count);

        Assert.Equal(501, bultos[0].Id);
        Assert.Equal("Pendiente", bultos[0].Estado);

        Assert.Equal(502, bultos[1].Id);
        Assert.Equal("Entregado", bultos[1].Estado);

        var regla = recuperada.ReglaEntregaParcial;

        Assert.NotNull(regla);
        Assert.Equal(2, regla.Version);
        Assert.True(regla.PermiteEntregaParcial);
        Assert.Equal(
            "Reprogramacion",
            regla.DestinoBultosPendientes);
    }

    private sealed class ConexionSimulada(string json)
        : HttpMessageHandler
    {
        public bool SinConexion { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (SinConexion)
                throw new HttpRequestException("Sin conexión");

            Assert.Equal(
                "/api/rutas/mias/10",
                request.RequestUri?.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
