namespace LastMileUY.DriverPWA.Services;

// Los bultos que el repartidor identificó con el escáner en cada parada de una hoja de ruta. Se
// guardan en el celular (AlmacenLocal, cifrado y con vencimiento) para que no se pierdan si se cierra
// la app a mitad de la entrega: al volver, el formulario arranca con los mismos bultos marcados.
// Van por hoja de ruta: si un bulto vuelve a salir en otra ruta, se identifica de nuevo.
public class BultosIdentificadosService(AlmacenLocal almacen)
{
    private static string Clave(int rutaId) => $"driver:bultos-identificados:{rutaId}";

    private readonly SemaphoreSlim semaforo = new(1, 1);

    // Por envío, los bultos identificados
    public async Task<Dictionary<int, HashSet<int>>> LeerAsync(int rutaId)
    {
        var guardados = await almacen.LeerAsync<Dictionary<int, List<int>>>(Clave(rutaId)) ?? [];

        return guardados.ToDictionary(x => x.Key, x => x.Value.ToHashSet());
    }

    public async Task GuardarAsync(int rutaId, int envioId, IReadOnlyCollection<int> bultosIds)
    {
        // El escáner puede avisar dos veces seguidas: que una escritura no pise a la otra
        await semaforo.WaitAsync();

        try
        {
            var guardados = await almacen.LeerAsync<Dictionary<int, List<int>>>(Clave(rutaId)) ?? [];

            if (bultosIds.Count == 0)
                guardados.Remove(envioId);
            else
                guardados[envioId] = bultosIds.Order().ToList();

            await almacen.GuardarAsync(Clave(rutaId), guardados, AlmacenLocal.DuracionMaxima);
        }
        finally
        {
            semaforo.Release();
        }
    }
}
