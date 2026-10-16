namespace LastMileUY.Backoffice.Servicios;

// Del formulario "Nueva hoja de ruta" a la lista ordenada de envíos que espera la API.
// En el formulario se tildan los envíos y, si se quiere, se les pone un número de orden.
public static class ArmadoDeRuta
{
    // Los tildados, ordenados por el número que se les puso; los que no tienen número van al final,
    // en el orden en que aparecen en la pantalla. Ante el mismo número, también decide la pantalla.
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
