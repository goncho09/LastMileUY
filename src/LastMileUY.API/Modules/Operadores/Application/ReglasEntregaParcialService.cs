using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Operadores;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Operadores.Application;

public class ReglasEntregaParcialService(ApplicationDbContext db, IContextoOperador contexto)
{
    private const int IntentosAlPublicar = 3;

    public async Task<IReadOnlyList<ReglaEntregaParcialDto>> ListarAsync(CancellationToken cancellationToken = default)
    {
        var filas = await (
                from r in db.ReglasEntregaParcial.AsNoTracking()
                join u in db.Users on r.PublicadaPorId equals u.Id into usuarios
                from u in usuarios.DefaultIfEmpty()
                orderby r.Version descending
                select new { Regla = r, PublicadaPor = u == null ? "—" : u.Nombre + " " + u.Apellido })
            .ToListAsync(cancellationToken);

        return filas.Select(x => ADto(x.Regla, x.PublicadaPor)).ToList();
    }

    public async Task<ReglaEntregaParcialDto> PublicarAsync(
        PublicarReglaEntregaParcialRequest request,
        string usuarioId,
        CancellationToken cancellationToken = default)
    {
        var operadorId = contexto.OperadorId
            ?? throw new UnauthorizedAccessException("No se pudo identificar el operador.");

        if (!Enum.TryParse<DestinoBultosPendientes>(request.DestinoBultosPendientes, ignoreCase: true, out var destino)
            || !Enum.IsDefined(destino))
        {
            throw new ArgumentException(
                $"El destino '{request.DestinoBultosPendientes}' no existe: tiene que ser NuevaHojaDeRuta o Devolucion.");
        }

        if (request.RecargoNuevaVisita < 0 ||
            request.RecargoNuevaVisita > 99999999.99m ||
            decimal.Round(request.RecargoNuevaVisita, 2) != request.RecargoNuevaVisita)
        {
            throw new ArgumentException(
                "El recargo debe ser positivo o cero y tener como máximo dos decimales.");
        }

        if (destino == DestinoBultosPendientes.Devolucion &&
            request.RecargoNuevaVisita != 0)
        {
            throw new ArgumentException(
                "No se puede configurar un recargo de nueva visita cuando el destino es devolución.");
        }

        var motivo = request.Motivo?.Trim();

        if (string.IsNullOrEmpty(motivo))
        {
            throw new ArgumentException("Contá por qué se cambian las reglas: queda en el historial.");
        }

        if (motivo.Length > ReglaEntregaParcial.LargoMaximoMotivo)
        {
            throw new ArgumentException(
                $"El motivo puede tener hasta {ReglaEntregaParcial.LargoMaximoMotivo} caracteres.");
        }

        for (var intento = 1; ; intento++)
        {
            var ultima = await db.ReglasEntregaParcial
                .MaxAsync(r => (int?)r.Version, cancellationToken) ?? 0;

            var regla = new ReglaEntregaParcial
            {
                OperadorId = operadorId,
                Version = ultima + 1,
                PermiteEntregaParcial = request.PermiteEntregaParcial,
                DestinoBultosPendientes = destino,
                RecargoNuevaVisita = request.RecargoNuevaVisita,
                Motivo = motivo,
                PublicadaPorId = usuarioId
            };

            db.ReglasEntregaParcial.Add(regla);

            try
            {
                await db.SaveChangesAsync(cancellationToken);

                var publicadaPor = await db.Users
                    .Where(u => u.Id == usuarioId)
                    .Select(u => u.Nombre + " " + u.Apellido)
                    .FirstOrDefaultAsync(cancellationToken);

                return ADto(regla, publicadaPor ?? "—");
            }
            catch (DbUpdateException ex) when (
                intento < IntentosAlPublicar
                && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(regla).State = EntityState.Detached;
            }
        }
    }

    private static ReglaEntregaParcialDto ADto(ReglaEntregaParcial regla, string publicadaPor) =>
        new(
            regla.Id,
            regla.Version,
            regla.PermiteEntregaParcial,
            regla.DestinoBultosPendientes.ToString(),
            regla.RecargoNuevaVisita,
            regla.Motivo,
            publicadaPor,
            regla.FechaPublicacion);
}
