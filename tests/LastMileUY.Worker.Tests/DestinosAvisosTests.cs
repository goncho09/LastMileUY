namespace LastMileUY.Worker.Tests;

using System.Net;
using LastMileUY.Contracts.Avisos;

// Qué URLs puede tener una suscripción: solo https y direcciones públicas (que no se pueda usar para
// llegar a servicios internos, SSRF)
public class DestinosAvisosTests
{
    [Theory]
    [InlineData("https://comercio.example/avisos")]
    [InlineData("https://8.8.8.8/avisos")]
    [InlineData("https://comercio.example:8443/avisos?token=1")]
    public void Acepta_una_url_https_publica(string url)
    {
        Assert.Null(DestinosAvisos.Validar(url, permitirLocales: false));
    }

    [Theory]
    [InlineData("no es una url", "no es válida")]
    [InlineData("/avisos", "no es válida")]
    [InlineData("http://comercio.example/avisos", "https")]
    [InlineData("ftp://comercio.example/avisos", "https")]
    [InlineData("https://usuario:clave@comercio.example/avisos", "usuario")]
    [InlineData("https://localhost/avisos", "propia máquina")]
    [InlineData("https://api.localhost/avisos", "propia máquina")]
    [InlineData("https://127.0.0.1/avisos", "propia máquina")]
    [InlineData("https://[::1]/avisos", "propia máquina")]
    [InlineData("https://10.0.0.5/avisos", "red interna")]
    [InlineData("https://192.168.1.10/avisos", "red interna")]
    [InlineData("https://169.254.169.254/latest/meta-data", "red interna")]
    public void Rechaza_las_que_no_son_https_o_apuntan_adentro(string url, string motivo)
    {
        Assert.Contains(motivo, DestinosAvisos.Validar(url, permitirLocales: false));
    }

    // Más larga que la columna: sin esto la base la cortaría con un error 500
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rechaza_una_url_mas_larga_que_el_maximo(bool permitirLocales)
    {
        var url = "https://comercio.example/" + new string('a', DestinosAvisos.LargoMaximo);

        Assert.Contains("superar", DestinosAvisos.Validar(url, permitirLocales));
    }

    [Theory]
    [InlineData("http://localhost:5085/avisos")]
    [InlineData("http://lastmileuy.receptoravisos:8080/avisos")]
    [InlineData("https://10.0.0.5/avisos")]
    public void Con_destinos_locales_acepta_http_y_direcciones_internas(string url)
    {
        Assert.Null(DestinosAvisos.Validar(url, permitirLocales: true));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void Distingue_las_direcciones_publicas(string direccion, bool publica)
    {
        Assert.Equal(publica, DestinosAvisos.EsPublica(IPAddress.Parse(direccion)));
    }
}
