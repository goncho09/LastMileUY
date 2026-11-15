using System.Net;
using System.Net.Http.Json;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;

namespace LastMileUY.API.Tests.PuntaAPunta;

using static ApiDePuntaAPunta;

[Collection(ColeccionApiDePuntaAPunta.Nombre)]
public class ImportacionArchivoDePuntaAPuntaTests(ApiDePuntaAPunta api)
{
    private const string Encabezado = "referencia;nombre;apellido;email;telefono;calle;numero;apartamento;ciudad;departamento;modalidad;bultos;peso_kg;largo_cm;ancho_cm;alto_cm;descripcion";

    private static async Task<ImportarEnviosResponse> ImportarAsync(HttpClient portal, string contenido)
    {
        using var respuesta = await portal.PostAsJsonAsync("api/envios/importacion", new ImportarArchivoRequest(contenido, "envios.csv"));
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ImportarEnviosResponse>())!;
    }

    [Fact]
    public async Task Reimportar_el_archivo_no_duplica_envios_ni_destinatarios()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var email = $"import-{sufijo}@correo.test";

        var archivo = string.Join("\r\n",
            Encabezado,
            $"IMP-{sufijo}-1;Ana;Silva;{email};099111222;Av. Brasil;2550;301;Montevideo;Montevideo;Estandar;2;2,5;30;20;15;Ropa",
            $"IMP-{sufijo}-2;Ana;Silva;{email};099111222;Av. Brasil;2550;301;Montevideo;Montevideo;;1;1.2;10;10;10;",
            $"IMP-{sufijo}-3;Luis;Pérez;;;\"Rambla; Rep. de México\";5100;;Montevideo;Montevideo;;1;0;10;10;10;",
            $";Sin;Referencia;;;Calle;1;;Montevideo;Montevideo;;1;1;1;1;1;",
            $"IMP-{sufijo}-5;Eva;Gómez;;;Calle;1;;Rivera;Atlantida;;1;1;10;10;10;");

        var primera = await ImportarAsync(portal, archivo);

        Assert.Equal(2, primera.Creados);
        Assert.Equal(3, primera.Rechazados);
        Assert.Equal([2, 3, 4, 5, 6], primera.Resultados.Select(r => r.Indice));
        Assert.Contains("peso", primera.Resultados.Single(r => r.Indice == 4).Mensaje);
        Assert.Contains("departamento", primera.Resultados.Single(r => r.Indice == 6).Mensaje);

        var creado = primera.Resultados.Single(r => r.Indice == 2);
        var detalle = (await portal.GetFromJsonAsync<EnvioDetalleDto>($"api/envios/{creado.EnvioId}"))!;
        Assert.Equal(2, detalle.Bultos.Count);

        var segunda = await ImportarAsync(portal, archivo);

        Assert.Equal(0, segunda.Creados);
        Assert.Equal(2, segunda.Existentes);
        Assert.Equal(creado.EnvioId, segunda.Resultados.Single(r => r.Indice == 2).EnvioId);

        var destinatarios = (await portal.GetFromJsonAsync<List<LastMileUY.Contracts.Destinatarios.DestinatarioDto>>("api/destinatarios"))!;
        var ana = Assert.Single(destinatarios, d => d.Email == email);
        Assert.Single(ana.Direcciones);
    }

    [Fact]
    public async Task Un_archivo_sin_las_columnas_obligatorias_se_rechaza_entero_y_solo_lo_usa_un_comercio()
    {
        var portal = await api.IniciarSesionAsync(Comercio1, api.IdOperador1);
        var operador = await api.IniciarSesionAsync(Operador1);

        using var sinColumnas = await portal.PostAsJsonAsync("api/envios/importacion",
            new ImportarArchivoRequest("referencia,nombre\nX,Y"));
        Assert.Equal(HttpStatusCode.BadRequest, sinColumnas.StatusCode);
        Assert.Contains("peso_kg", await sinColumnas.Content.ReadAsStringAsync());

        using var desdeOperador = await operador.PostAsJsonAsync("api/envios/importacion",
            new ImportarArchivoRequest(Encabezado + "\nX;Y"));
        Assert.Equal(HttpStatusCode.Forbidden, desdeOperador.StatusCode);
    }
}
