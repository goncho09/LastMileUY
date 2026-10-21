using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

public class ColaOperacionesService(AlmacenLocal almacen)
{
    private const string ClaveCola = "driver:operaciones-pendientes";

    private readonly SemaphoreSlim semaforo = new(1, 1);

    public async Task<List<OperacionRequest>> ObtenerPendientesAsync()
    {
        await semaforo.WaitAsync();

        try
        {
            return await LeerColaAsync();
        }
        finally
        {
            semaforo.Release();
        }
    }

    public async Task<OperacionRequest> AgregarAsync(OperacionRequest operacion)
    {
        await semaforo.WaitAsync();

        try
        {
            var pendientes = await LeerColaAsync();

            // Cada acción nueva recibe su propio identificador.
            if (operacion.IdOperacion == Guid.Empty)
                operacion.IdOperacion = Guid.NewGuid();

            // Evitar duplicar una operación ya registrada.
            var existente = pendientes.FirstOrDefault(x =>
                x.IdOperacion == operacion.IdOperacion);

            if (existente is not null)
                return existente;

            pendientes.Add(operacion);

            await GuardarColaAsync(pendientes);

            return operacion;
        }
        finally
        {
            semaforo.Release();
        }
    }

    public async Task EliminarAsync(Guid idOperacion)
    {
        await semaforo.WaitAsync();

        try
        {
            var pendientes = await LeerColaAsync();

            pendientes.RemoveAll(x =>
                x.IdOperacion == idOperacion);

            await GuardarColaAsync(pendientes);
        }
        finally
        {
            semaforo.Release();
        }
    }

    private async Task<List<OperacionRequest>> LeerColaAsync()
    {
        return await almacen
            .LeerPendienteAsync<List<OperacionRequest>>(ClaveCola)
            ?? [];
    }

    private Task GuardarColaAsync(List<OperacionRequest> pendientes)
    {
        return almacen.GuardarPendienteAsync(ClaveCola, pendientes);
    }
}