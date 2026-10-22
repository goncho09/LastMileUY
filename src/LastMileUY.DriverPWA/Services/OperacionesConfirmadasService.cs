namespace LastMileUY.DriverPWA.Services;

public class OperacionesConfirmadasService(AlmacenLocal almacen)
{
    private const string Clave = "driver:entregas-confirmadas";

    private readonly SemaphoreSlim semaforo = new(1, 1);

    public async Task<bool> EstaConfirmadoAsync(int envioId)
    {
        await semaforo.WaitAsync();

        try
        {
            var entregados = await LeerAsync();
            return entregados.Contains(envioId);
        }
        finally
        {
            semaforo.Release();
        }
    }

    public async Task RegistrarAsync(int envioId)
    {
        await semaforo.WaitAsync();

        try
        {
            var entregados = await LeerAsync();

            if (entregados.Add(envioId))
            {
                await almacen.GuardarPendienteAsync(
                    Clave,
                    entregados);
            }
        }
        finally
        {
            semaforo.Release();
        }
    }

    private async Task<HashSet<int>> LeerAsync()
    {
        return await almacen.LeerPendienteAsync<HashSet<int>>(Clave)
               ?? [];
    }
}