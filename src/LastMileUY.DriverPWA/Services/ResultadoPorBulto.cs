using LastMileUY.Contracts.Entregas;
using LastMileUY.Contracts.Rutas;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

// Qué se manda al confirmar una entrega cuando la parada tiene varios bultos: el repartidor marca
// cuáles entregó y, de los que no, por qué. Con eso se decide el tipo de operación:
// - todos entregados: Entrega, con la lista de los bultos entregados;
// - algunos: EntregaParcial, con el resultado de cada bulto que faltaba entregar;
// - ninguno: no es una entrega, es "No se pudo entregar".
// Las mismas reglas las vuelve a controlar la API (docs/sincronizacion.md); acá es para avisarle
// al repartidor antes de guardar, también sin conexión.
public static class ResultadoPorBulto
{
    public sealed class Marca
    {
        public bool Entregado { get; set; }

        public int? MotivoNoEntregaId { get; set; }
    }

    public sealed record Decision(string? Error, string Tipo, List<ResultadoBultoRequest>? Resultados);

    // Los que todavía hay que entregar: los Pendientes y los que no se entregaron en una ruta anterior
    public static bool PorEntregar(BultoRutaDto bulto) =>
        bulto.Estado is "Pendiente" or "NoEntregado";

    // Cómo arranca cada bulto: si el repartidor escaneó alguno, esos como entregados y el resto no;
    // si no escaneó ninguno, todos entregados. Después los puede cambiar a mano.
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

        // Sin la lista de bultos (una hoja descargada antes de que viniera) es una entrega común
        if (porEntregar.Count == 0)
        {
            return new Decision(null, "Entrega", null);
        }

        // Todos entregados: una entrega, con la lista de los bultos que vio el repartidor. Así la API
        // no da por entregado uno que no estaba en la hoja del celular (sería un conflicto)
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

    // Qué quedó guardado de cada bulto en una operación que todavía no llegó a la API, para mostrarlo
    // en la hoja de ruta (también después de cerrar y volver a abrir la app). Vacío si la operación
    // no trae resultados por bulto.
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

    // Un bulto sin marca no cuenta como entregado: nunca se da por entregado algo que el repartidor
    // no vio (no debería pasar, las marcas salen de la misma lista)
    private static Marca Marcado(BultoRutaDto bulto, IReadOnlyDictionary<int, Marca> marcas) =>
        marcas.TryGetValue(bulto.Id, out var marca) ? marca : new Marca { Entregado = false };
}
