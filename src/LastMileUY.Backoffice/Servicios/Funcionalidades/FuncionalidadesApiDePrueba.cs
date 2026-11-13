using LastMileUY.Contracts.Operadores;

namespace LastMileUY.Backoffice.Servicios;

public class FuncionalidadesApiDePrueba : IFuncionalidadesApi
{
    private readonly Dictionary<(int OperadorId, string Clave), bool> decisiones = [];
    private readonly Lock candado = new();

    private static readonly (int Id, string Nombre)[] Operadores =
        [(1, "Operador de prueba"), (2, "Otro operador")];

    public Task<IReadOnlyList<FuncionalidadesOperadorDto>> ListarAsync(CancellationToken ct = default)
    {
        lock (candado)
        {
            IReadOnlyList<FuncionalidadesOperadorDto> lista = Operadores
                .Select(o => new FuncionalidadesOperadorDto(o.Id, o.Nombre, Funcionalidades.Todas
                    .Select(f => new FuncionalidadDto(f.Clave, f.Nombre, f.Descripcion,
                        decisiones.GetValueOrDefault((o.Id, f.Clave), f.PorDefecto), f.PorDefecto))
                    .ToList()))
                .ToList();

            return Task.FromResult(lista);
        }
    }

    public Task CambiarAsync(int operadorId, string clave, bool activa, CancellationToken ct = default)
    {
        if (Operadores.All(o => o.Id != operadorId))
            throw new OperacionRechazadaException("Ese operador no existe.");

        if (Funcionalidades.Buscar(clave) is null)
            throw new OperacionRechazadaException($"No existe la funcionalidad '{clave}'.");

        lock (candado)
        {
            decisiones[(operadorId, clave)] = activa;
        }

        return Task.CompletedTask;
    }
}
