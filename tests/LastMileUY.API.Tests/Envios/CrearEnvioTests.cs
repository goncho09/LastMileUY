namespace LastMileUY.API.Tests.Envios;

using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Tests.Multitenancy;
using Microsoft.EntityFrameworkCore;
using static LastMileUY.API.Tests.Multitenancy.BaseConDosOperadores;

// Crear un envío completo con EF contra la base real, como lo hará el alta de envíos
[Collection(ColeccionBaseConDosOperadores.Nombre)]
public class CrearEnvioTests(BaseConDosOperadores baseDeDatos)
{
    [Fact]
    public async Task Un_envio_se_guarda_con_su_direccion_y_sus_bultos()
    {
        var envio = new Envio
        {
            ComercioId = 1,
            DestinatarioId = 1,
            DireccionId = 1,
            ReferenciaExterna = "PED-PRUEBA-1"
        };
        envio.AsignarDireccionEntrega(new DireccionEntrega(
            "Av. Italia", "2500", "Apto 3", "Montevideo", "Montevideo", "11600", "Portón verde"));
        envio.Bultos.Add(new Bulto { Codigo = "B1", PesoKg = 2.5m, LargoCm = 30, AnchoCm = 20, AltoCm = 10 });

        await using (var db = baseDeDatos.CrearContexto(OperadorA))
        {
            db.Envios.Add(envio);
            await db.SaveChangesAsync();
        }

        await using var otra = baseDeDatos.CrearContexto(OperadorA);
        var guardado = await otra.Envios.Include(e => e.Bultos).SingleAsync(e => e.Id == envio.Id);

        Assert.Equal(OperadorA, guardado.OperadorId);
        Assert.Equal("Av. Italia", guardado.DireccionEntrega.Calle);
        Assert.Equal("Portón verde", guardado.DireccionEntrega.Referencia);
        Assert.Equal(12, guardado.CodigoSeguimiento.Length);
        Assert.Single(guardado.Bultos);
    }

    [Fact]
    public void La_direccion_de_entrega_no_puede_ser_nula()
    {
        var envio = new Envio();

        Assert.Throws<ArgumentNullException>(() => envio.AsignarDireccionEntrega(null!));
    }
}
