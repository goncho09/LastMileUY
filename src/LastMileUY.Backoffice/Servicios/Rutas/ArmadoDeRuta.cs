namespace LastMileUY.Backoffice.Servicios;

public static class ArmadoDeRuta
{
    public static List<int> OrdenarSeleccion(
        IReadOnlyList<int> enPantalla,
        IReadOnlyCollection<int> seleccionados,
        IReadOnlyDictionary<int, int?> orden) =>
        enPantalla
            .Select((id, posicion) => (Id: id, Posicion: posicion))
            .Where(x => seleccionados.Contains(x.Id))
            .OrderBy(x => orden.GetValueOrDefault(x.Id) ?? int.MaxValue)
            .ThenBy(x => x.Posicion)
            .Select(x => x.Id)
            .ToList();
}
