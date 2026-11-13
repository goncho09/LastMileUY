using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public interface IFuncionalidadesApi
{
    Task<IReadOnlyList<FuncionalidadesOperadorDto>> ListarAsync(CancellationToken ct = default);

    Task CambiarAsync(int operadorId, string clave, bool activa, CancellationToken ct = default);
}
