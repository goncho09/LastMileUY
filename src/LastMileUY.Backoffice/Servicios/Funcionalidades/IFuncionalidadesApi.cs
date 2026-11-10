using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

// Banderas de funcionalidad por operador (/api/admin): solo para el Administrador de la plataforma
public interface IFuncionalidadesApi
{
    Task<IReadOnlyList<FuncionalidadesOperadorDto>> ListarAsync(CancellationToken ct = default);

    // OperacionRechazadaException con el motivo si la API no lo acepta
    Task CambiarAsync(int operadorId, string clave, bool activa, CancellationToken ct = default);
}
