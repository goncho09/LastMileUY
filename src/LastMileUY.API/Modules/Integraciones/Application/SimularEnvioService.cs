using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Integraciones.Application;

// Ambiente de pruebas de la API pública: en pruebas no hay depósito, rutas ni repartidores, así que un
// envío de prueba quedaría siempre "Admitido". Esto lo hace avanzar por el recorrido normal
// (EnDeposito → AsignadoARuta → EnTransito → Entregado o NoEntregado), para que el comercio vea en su
// sistema cada cambio de estado, igual que en un envío de verdad.
// Cada paso pasa por Envio.CambiarEstado: vale la misma tabla de transiciones (req. 11) y queda en el
// historial con origen "Simulación".
// Solo con una clave de prueba: el endpoint lo exige y además se controla acá, y el envío tiene que ser
// del operador de pruebas. Una clave de verdad nunca puede cambiar el estado de un envío.
public class SimularEnvioService(
    ApplicationDbContext db,
    IContextoOperador contexto,
    AmbientePruebasService ambientePruebas)
{
    public const string Origen = "Simulación";

    // El recorrido de un envío hasta salir a entregar
    private static readonly EstadoEnvio[] Recorrido =
        [EstadoEnvio.Admitido, EstadoEnvio.EnDeposito, EstadoEnvio.AsignadoARuta, EstadoEnvio.EnTransito];

    public static readonly string[] EstadosSimulables =
    [
        nameof(EstadoEnvio.EnDeposito), nameof(EstadoEnvio.AsignadoARuta), nameof(EstadoEnvio.EnTransito),
        nameof(EstadoEnvio.Entregado), nameof(EstadoEnvio.NoEntregado)
    ];

    // false si el envío no existe (o no es del ambiente de pruebas: para quien pregunta, es lo mismo)
    public async Task<bool> SimularAsync(int envioId, string? estado, CancellationToken ct)
    {
        if (!contexto.EnAmbientePruebas)
            throw new UnauthorizedAccessException("La simulación es solo para claves del ambiente de pruebas.");

        if (!EstadosSimulables.Contains(estado) || !Enum.TryParse<EstadoEnvio>(estado, out var objetivo))
            throw new ArgumentException($"El estado a simular tiene que ser uno de: {string.Join(", ", EstadosSimulables)}.");

        var operadorPruebas = await ambientePruebas.OperadorIdAsync(ct);

        var envio = await db.Envios
            .Include(e => e.IntentosEntrega)
            .Include(e => e.Bultos)
            .FirstOrDefaultAsync(e => e.Id == envioId && e.OperadorId == operadorPruebas, ct);

        if (envio is null)
            return false;

        var esFinal = objetivo is EstadoEnvio.Entregado or EstadoEnvio.NoEntregado;
        var desde = Array.IndexOf(Recorrido, envio.Estado);
        var hasta = Array.IndexOf(Recorrido, esFinal ? EstadoEnvio.EnTransito : objetivo);

        if (desde < 0 || desde > hasta || (desde == hasta && !esFinal))
            throw new ArgumentException($"El envío está en {envio.Estado}: no se puede simular {objetivo}.");

        try
        {
            for (var paso = desde + 1; paso <= hasta; paso++)
                envio.CambiarEstado(Recorrido[paso], Origen, "Simulado en el ambiente de pruebas.");

            if (esFinal)
            {
                var estadoBultos = objetivo == EstadoEnvio.Entregado ? EstadoBulto.Entregado : EstadoBulto.NoEntregado;

                foreach (var bulto in envio.Bultos.Where(b => b.Estado == EstadoBulto.Pendiente))
                    bulto.CambiarEstado(estadoBultos);

                envio.CambiarEstado(objetivo, Origen, "Simulado en el ambiente de pruebas.");
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
