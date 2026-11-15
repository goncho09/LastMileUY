using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;
using LastMileUY.DriverPWA.Services;

namespace LastMileUY.DriverPWA.Tests;

public class RendicionServiceTests
{
    private static ParadaDto Parada(int orden, int envioId, string estado, params BultoRutaDto[] bultos) =>
        new(orden, envioId, $"COD{envioId}", estado,
            new DestinatarioDto("Ana", "Silva", null, null),
            new DireccionDto("Calle", "1", null, "Montevideo", "Montevideo", null, null),
            null, bultos.Length, 1, 0.01m, "Estandar", EvidenciaRequeridaDto.Ninguna, bultos);

    private static BultoRutaDto Bulto(int id, string estado = "Pendiente") => new(id, $"B{id}", estado, null, 1);

    private static RutaDetalleDto Ruta(params ParadaDto[] paradas) =>
        new(1, "Ruta", DateOnly.FromDateTime(DateTime.Today), "EnCurso",
            new RepartidorDto(1, "Repartidor"), new VehiculoDto(1, "SBC1234", "Camioneta", 500, 5), 1, 0.01m, paradas);

    [Fact]
    public void Vuelve_lo_que_no_se_entrego_segun_el_servidor_y_lo_pendiente_en_el_celular()
    {
        var ruta = Ruta(
            Parada(1, 10, "Entregado", Bulto(1, "Entregado")),
            Parada(2, 20, "NoEntregado", Bulto(2, "NoEntregado")),
            Parada(3, 30, "EnTransito", Bulto(3), Bulto(4)),
            Parada(4, 40, "EnTransito", Bulto(5)),
            Parada(5, 50, "EnTransito", Bulto(6)));

        var pendientes = new Dictionary<int, OperacionRequest>
        {
            [30] = new() { Tipo = "EntregaParcial", ResultadosBultos = [new(3, true), new(4, false, 1)] },
            [40] = new() { Tipo = "Entrega" }
        };

        var codigos = RendicionService.BultosADevolver(ruta, pendientes).Select(b => b.Bulto.Codigo).ToList();

        Assert.Equal(["B2", "B4", "B6"], codigos);
    }
}
