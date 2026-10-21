using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.DriverPWA.Services;

public record EstadoEnvioOperacion(
    bool Pendiente,
    ResultadoOperacionDto? Resultado
);