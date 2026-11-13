using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Configuracion;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

public class ReglasOperativasService(ApplicationDbContext db, IContextoOperador contexto)
{
    private const string Repetida = "Otra regla de esa modalidad se activó al mismo tiempo. Probá de nuevo.";

    public async Task<IReadOnlyList<ReglaOperativaConfiguracionDto>> ListarAsync(CancellationToken ct = default)
    {
        var reglas = await db.ReglasOperativas.AsNoTracking()
            .OrderBy(r => r.Modalidad)
            .ThenByDescending(r => r.Activa)
            .ThenBy(r => r.Nombre)
            .ToListAsync(ct);

        return reglas.Select(ADto).ToList();
    }

    public async Task<ReglaOperativaConfiguracionDto> CrearAsync(GuardarReglaOperativaRequest request, CancellationToken ct = default)
    {
        var regla = new ReglaOperativa { OperadorId = contexto.OperadorActual(), Activa = true };
        Aplicar(regla, request);

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        await DesactivarOtrasAsync(regla.Modalidad, null, ct);
        db.ReglasOperativas.Add(regla);
        await db.GuardarAsync(Repetida, ct);
        await transaccion.CommitAsync(ct);

        return ADto(regla);
    }

    public async Task<bool> EditarAsync(int id, GuardarReglaOperativaRequest request, CancellationToken ct = default)
    {
        var regla = await db.ReglasOperativas.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (regla is null)
            return false;

        Aplicar(new ReglaOperativa(), request);

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        if (regla.Activa)
            await DesactivarOtrasAsync(ConfiguracionComun.Modalidad(request.Modalidad), regla.Id, ct);
        Aplicar(regla, request);
        await db.GuardarAsync(Repetida, ct);
        await transaccion.CommitAsync(ct);
        return true;
    }

    public async Task<bool> CambiarActivaAsync(int id, bool activa, CancellationToken ct = default)
    {
        var regla = await db.ReglasOperativas.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (regla is null)
            return false;

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        if (activa)
            await DesactivarOtrasAsync(regla.Modalidad, regla.Id, ct);
        regla.Activa = activa;
        await db.GuardarAsync(Repetida, ct);
        await transaccion.CommitAsync(ct);
        return true;
    }

    private async Task DesactivarOtrasAsync(ModalidadEnvio modalidad, int? excepto, CancellationToken ct)
    {
        var otras = await db.ReglasOperativas
            .Where(r => r.Activa && r.Modalidad == modalidad && r.Id != excepto)
            .ToListAsync(ct);

        if (otras.Count == 0)
            return;

        foreach (var otra in otras)
            otra.Activa = false;

        await db.SaveChangesAsync(ct);
    }

    private static void Aplicar(ReglaOperativa regla, GuardarReglaOperativaRequest request)
    {
        regla.Nombre = ConfiguracionComun.Requerido(request.Nombre, "el nombre", 150);
        regla.Descripcion = ConfiguracionComun.Opcional(request.Descripcion, "la descripción", 500);
        regla.Modalidad = ConfiguracionComun.Modalidad(request.Modalidad);
        regla.MaximoIntentosEntrega = ConfiguracionComun.Entero(request.MaximoIntentosEntrega, "el máximo de intentos", 1, 10);
        regla.HorasEntreIntentos = ConfiguracionComun.Entero(request.HorasEntreIntentos, "las horas entre intentos", 0, 720);
        regla.PlazoEntregaHoras = ConfiguracionComun.Entero(request.PlazoEntregaHoras, "el plazo de entrega", 1, 720);
        regla.HorasParaIniciarDevolucion = ConfiguracionComun.Entero(request.HorasParaIniciarDevolucion, "las horas para iniciar la devolución", 0, 720);
        regla.RequiereFirma = request.RequiereFirma;
        regla.RequiereFotografia = request.RequiereFotografia;
        regla.RequiereDocumentoReceptor = request.RequiereDocumentoReceptor;
    }

    private static ReglaOperativaConfiguracionDto ADto(ReglaOperativa r) =>
        new(
            r.Id,
            r.Nombre,
            r.Descripcion,
            r.Modalidad.ToString(),
            r.MaximoIntentosEntrega,
            r.HorasEntreIntentos,
            r.PlazoEntregaHoras,
            r.HorasParaIniciarDevolucion,
            r.RequiereFirma,
            r.RequiereFotografia,
            r.RequiereDocumentoReceptor,
            r.Activa);
}
