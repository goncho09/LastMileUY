namespace LastMileUY.DriverPWA.Services;

public class BultosIdentificadosService(AlmacenLocal almacen)
{
    private static string Clave(int rutaId) => $"driver:bultos-identificados:{rutaId}";

    private readonly SemaphoreSlim semaforo = new(1, 1);

    public async Task<Dictionary<int, HashSet<int>>> LeerAsync(int rutaId)
    {
        var guardados = await almacen.LeerAsync<Dictionary<int, List<int>>>(Clave(rutaId)) ?? [];

        return guardados.ToDictionary(x => x.Key, x => x.Value.ToHashSet());
    }

    public async Task GuardarAsync(int rutaId, int envioId, IReadOnlyCollection<int> bultosIds)
    {
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
