using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

public static class ResultadoPorBulto
{
    public sealed class Marca
    {
        public bool Entregado { get; set; }

        public int? MotivoNoEntregaId { get; set; }
    }

    public sealed record Decision(string? Error, string Tipo, List<ResultadoBultoRequest>? Resultados);

    public static bool PorEntregar(BultoRutaDto bulto) =>
        bulto.Estado is "Pendiente" or "NoEntregado";

    public static Dictionary<int, Marca> MarcasIniciales(
        IEnumerable<BultoRutaDto> bultos,
        IReadOnlyCollection<int> identificados)
    {
        return bultos
            .Where(PorEntregar)
            .ToDictionary(
                b => b.Id,
                b => new Marca
                {
                    Entregado = identificados.Contains(b.Id)
                });
    }

    public static Decision Decidir(
        IEnumerable<BultoRutaDto> bultos,
        IReadOnlyDictionary<int, Marca> marcas,
        ReglaEntregaParcialRutaDto? regla)
    {
        var porEntregar = bultos.Where(PorEntregar).ToList();

        if (porEntregar.Count == 0)
        {
            return new Decision(null, "Entrega", null);
        }

        if (porEntregar.All(b => Marcado(b, marcas).Entregado))
        {
            return new Decision(
                null,
                "Entrega",
                porEntregar.Select(b => new ResultadoBultoRequest(b.Id, Entregado: true)).ToList());
        }

        if (porEntregar.All(b => !Marcado(b, marcas).Entregado))
        {
            return new Decision(
                "No marcaste ningún bulto como entregado. Si no pudiste entregar, usá \"No se pudo entregar\".",
                "Entrega",
                null);
        }

        if (regla is { PermiteEntregaParcial: false })
        {
            return new Decision(
                $"Las reglas de esta hoja de ruta (versión {regla.Version}) no permiten entregas parciales: " +
                "entregá todos los bultos o registrá que no se pudo entregar.",
                "Entrega",
                null);
        }

        if (porEntregar.Any(b => Marcado(b, marcas) is { Entregado: false, MotivoNoEntregaId: null }))
        {
            return new Decision("Elegí el motivo de cada bulto que no entregaste.", "EntregaParcial", null);
        }

        var resultados = porEntregar
            .Select(b => Marcado(b, marcas) is { Entregado: true }
                ? new ResultadoBultoRequest(b.Id, Entregado: true)
                : new ResultadoBultoRequest(b.Id, Entregado: false, Marcado(b, marcas).MotivoNoEntregaId))
            .ToList();

        return new Decision(null, "EntregaParcial", resultados);
    }

    public static List<string> Resumen(
        OperacionRequest operacion,
        IEnumerable<BultoRutaDto> bultos,
        IEnumerable<MotivoNoEntregaDto> motivos)
    {
        var codigos = bultos.ToDictionary(b => b.Id, b => b.Codigo);
        var nombres = motivos.ToDictionary(m => m.Id, m => m.Nombre);

        return (operacion.ResultadosBultos ?? [])
            .Select(r =>
            {
                var codigo = codigos.GetValueOrDefault(r.BultoId) ?? $"Bulto {r.BultoId}";

                if (r.Entregado)
                    return $"{codigo}: entregado";

                return r.MotivoNoEntregaId is { } motivo && nombres.TryGetValue(motivo, out var nombre)
                    ? $"{codigo}: no entregado ({nombre})"
                    : $"{codigo}: no entregado";
            })
            .ToList();
    }

    private static Marca Marcado(BultoRutaDto bulto, IReadOnlyDictionary<int, Marca> marcas) =>
        marcas.TryGetValue(bulto.Id, out var marca) ? marca : new Marca { Entregado = false };
}
