using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Infrastructure.Persistence.Configurations;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Rutas.Application;

public class PlanificarRutaService(ApplicationDbContext db)
{
    public const int MaximoParadas = 100;

    private static readonly EstadoEnvio[] EstadosAsignables = [EstadoEnvio.EnDeposito, EstadoEnvio.Reprogramado];

    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    private static bool EsPendiente(Bulto bulto) =>
        bulto.Estado is EstadoBulto.Pendiente or EstadoBulto.NoEntregado;

    public async Task<int> CrearAsync(
        CrearRutaRequest request,
        string? responsableId,
        CancellationToken cancellationToken = default)
    {
        var nombre = request.Nombre?.Trim() ?? string.Empty;

        if (nombre.Length == 0)
        {
            throw new ArgumentException("La ruta necesita un nombre.");
        }

        if (nombre.Length > 150)
        {
            throw new ArgumentException("El nombre de la ruta no puede superar los 150 caracteres.");
        }

        if (request.Fecha < Hoy())
        {
            throw new ArgumentException("La fecha de la ruta no puede ser anterior a hoy.");
        }

        var envioIds = request.EnvioIds ?? [];

        if (envioIds.Count == 0)
        {
            throw new ArgumentException("La ruta tiene que tener al menos un envío.");
        }

        if (envioIds.Count > MaximoParadas)
        {
            throw new ArgumentException($"Una ruta puede tener hasta {MaximoParadas} paradas.");
        }

        var repetido = envioIds.GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);

        if (repetido is not null)
        {
            throw new ArgumentException($"El envío {repetido.Key} está más de una vez en la ruta.");
        }

        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        var repartidor = await BloqueoRepartidor.ObtenerAsync(
                             db,
                             request.RepartidorId,
                             cancellationToken)
                         ?? throw new ArgumentException("El repartidor no existe.");

        var usuarioActivo = await db.Users
            .AnyAsync(u =>
                    u.Id == repartidor.UsuarioId &&
                    u.Activo,
                cancellationToken);

        if (!repartidor.Activo || !usuarioActivo)
        {
            throw new ArgumentException("El repartidor está dado de baja.");
        }

        var vehiculo = await db.Vehiculos
            .FirstOrDefaultAsync(v => v.Id == request.VehiculoId, cancellationToken)
            ?? throw new ArgumentException("El vehículo no existe.");

        if (!vehiculo.Activo)
        {
            throw new ArgumentException("El vehículo está dado de baja.");
        }

        var rutasDelDia = db.Rutas.Where(r => r.Fecha == request.Fecha && Ruta.EstadosAbiertos.Contains(r.Estado));

        if (await rutasDelDia.AnyAsync(r => r.RepartidorId == repartidor.Id, cancellationToken))
        {
            throw new AsignacionDuplicadaException(
                $"El repartidor ya tiene una ruta para el {request.Fecha:dd/MM/yyyy}.");
        }

        if (await rutasDelDia.AnyAsync(r => r.VehiculoId == vehiculo.Id, cancellationToken))
        {
            throw new AsignacionDuplicadaException(
                $"El vehículo {vehiculo.Matricula} ya está en una ruta el {request.Fecha:dd/MM/yyyy}.");
        }

        var envios = await db.Envios
            .Include(e => e.Bultos)
            .Where(e => envioIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var faltante = envioIds.FirstOrDefault(id => !envios.ContainsKey(id));

        if (faltante != 0)
        {
            throw new ArgumentException($"El envío {faltante} no existe.");
        }

        var enOtraRuta = await (
                from p in db.Paradas
                join r in db.Rutas on p.RutaId equals r.Id
                where envioIds.Contains(p.EnvioId) && Ruta.EstadosAbiertos.Contains(r.Estado)
                select new { p.EnvioId, r.Nombre })
            .FirstOrDefaultAsync(cancellationToken);

        if (enOtraRuta is not null)
        {
            throw new AsignacionDuplicadaException(
                $"El envío {envios[enOtraRuta.EnvioId].CodigoSeguimiento} ya está en la ruta \"{enOtraRuta.Nombre}\".");
        }

        foreach (var envio in envios.Values)
        {
            if (envio.Estado == EstadoEnvio.AsignadoARuta)
            {
                throw new AsignacionDuplicadaException(
                    $"El envío {envio.CodigoSeguimiento} ya está asignado a una ruta.");
            }

            if (!EstadosAsignables.Contains(envio.Estado))
            {
                throw new ArgumentException(
                    $"El envío {envio.CodigoSeguimiento} está {envio.Estado}: "
                    + "solo se pueden asignar envíos en depósito o reprogramados.");
            }

            if (!envio.Bultos.Any(EsPendiente))
            {
                throw new ArgumentException(
                    $"El envío {envio.CodigoSeguimiento} no tiene bultos pendientes de entrega.");
            }
        }

        ValidarCapacidad(vehiculo, envios.Values);
        await ValidarFranjasAsync(envioIds.Select(id => envios[id]).ToList(), cancellationToken);

        var ruta = new Ruta
        {
            Nombre = nombre,
            Fecha = request.Fecha,
            RepartidorId = repartidor.Id,
            VehiculoId = vehiculo.Id
        };

        for (var i = 0; i < envioIds.Count; i++)
        {
            var envio = envios[envioIds[i]];

            var pendientes = envio.Bultos.Where(EsPendiente).ToList();
            var parada = new Parada { EnvioId = envio.Id, Orden = i + 1 };

            foreach (var bulto in pendientes)
            {
                parada.Bultos.Add(new BultoParada { BultoId = bulto.Id });
            }

            ruta.Paradas.Add(parada);

            var descripcion = pendientes.Count == envio.Bultos.Count
                ? $"Asignado a la ruta \"{nombre}\" (parada {i + 1})."
                : $"Asignado a la ruta \"{nombre}\" (parada {i + 1}) con los {pendientes.Count} bultos "
                  + $"pendientes de {envio.Bultos.Count}.";

            envio.CambiarEstado(
                EstadoEnvio.AsignadoARuta,
                "Backoffice",
                descripcion,
                responsableId);
        }

        db.Rutas.Add(ruta);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaccion.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: RutaConfiguration.IndiceRepartidorPorDia
                or RutaConfiguration.IndiceVehiculoPorDia
            } choque)
        {
            throw new AsignacionDuplicadaException(
                choque.ConstraintName == RutaConfiguration.IndiceRepartidorPorDia
                    ? $"El repartidor ya tiene una ruta para el {request.Fecha:dd/MM/yyyy}."
                    : $"El vehículo {vehiculo.Matricula} ya está en una ruta el {request.Fecha:dd/MM/yyyy}.");
        }

        return ruta.Id;
    }

    public async Task<bool> DespacharAsync(
        int id,
        string? responsableId,
        CancellationToken cancellationToken = default)
    {
        await using var transaccion =
            await db.Database.BeginTransactionAsync(cancellationToken);

        var ruta = await db.Rutas
            .Include(r => r.Paradas)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (ruta is null)
        {
            return false;
        }

        var repartidor = await BloqueoRepartidor.ObtenerAsync(
            db,
            ruta.RepartidorId,
            cancellationToken);

        if (repartidor is null)
            throw new EstadoDeRutaException(
                "El repartidor asignado no existe.");

        var usuarioActivo = await db.Users
            .AnyAsync(u =>
                    u.Id == repartidor.UsuarioId &&
                    u.Activo,
                cancellationToken);

        if (!repartidor.Activo || !usuarioActivo)
            throw new EstadoDeRutaException(
                "No se puede despachar una ruta con un repartidor inactivo.");

        var envioIds = ruta.Paradas.Select(p => p.EnvioId).ToList();
        var envios = await db.Envios
            .Where(e => envioIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var reglaEntregaParcial = await db.ObtenerReglaEntregaParcialVigenteAsync(cancellationToken);

        try
        {
            ruta.Despachar(reglaEntregaParcial);

            var desasignado = envios.FirstOrDefault(e => e.Estado != EstadoEnvio.AsignadoARuta);

            if (desasignado is not null)
            {
                throw new InvalidOperationException(
                    $"El envío {desasignado.CodigoSeguimiento} ya no está asignado a la ruta (está {desasignado.Estado}).");
            }

            foreach (var envio in envios)
            {
                envio.CambiarEstado(
                    EstadoEnvio.EnTransito,
                    "Backoffice",
                    $"Salió en la ruta \"{ruta.Nombre}\".",
                    responsableId);
            }
        }
        catch (InvalidOperationException ex)
        {
            throw new EstadoDeRutaException(ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaccion.CommitAsync(cancellationToken);

        return true;
    }

    private static void ValidarCapacidad(Vehiculo vehiculo, IEnumerable<Envio> envios)
    {
        var bultos = envios
            .SelectMany(e => e.Bultos)
            .Where(EsPendiente)
            .ToList();

        var peso = bultos.Sum(b => b.PesoKg);
        var volumen = bultos.Sum(b => b.VolumenM3);

        if (peso > vehiculo.CapacidadPesoKg)
        {
            throw new ArgumentException(
                $"Los envíos pesan {peso:0.##} kg y el vehículo {vehiculo.Matricula} carga hasta {vehiculo.CapacidadPesoKg:0.##} kg.");
        }

        if (volumen > vehiculo.CapacidadVolumenM3)
        {
            throw new ArgumentException(
                $"Los envíos ocupan {volumen:0.###} m³ y el vehículo {vehiculo.Matricula} tiene {vehiculo.CapacidadVolumenM3:0.###} m³.");
        }
    }

    private async Task ValidarFranjasAsync(IReadOnlyList<Envio> enOrden, CancellationToken cancellationToken)
    {
        var franjaIds = enOrden
            .Where(e => e.FranjaHorariaId is not null)
            .Select(e => e.FranjaHorariaId!.Value)
            .Distinct()
            .ToList();

        if (franjaIds.Count == 0)
        {
            return;
        }

        var franjas = await db.FranjasHorarias
            .Where(f => franjaIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

        (int Orden, string Nombre, TimeOnly Inicio)? anterior = null;

        for (var i = 0; i < enOrden.Count; i++)
        {
            var envio = enOrden[i];

            if (envio.FranjaHorariaId is not { } franjaId)
            {
                continue;
            }

            if (!franjas.TryGetValue(franjaId, out var franja) || !franja.Activa)
            {
                throw new ArgumentException(
                    $"La franja horaria del envío {envio.CodigoSeguimiento} no está disponible.");
            }

            if (anterior is { } previa && franja.HoraInicio < previa.Inicio)
            {
                throw new ArgumentException(
                    $"La parada {i + 1} ({envio.CodigoSeguimiento}, franja \"{franja.Nombre}\") no puede ir "
                    + $"después de la parada {previa.Orden} (franja \"{previa.Nombre}\"): "
                    + "las paradas con franja van en orden de horario.");
            }

            anterior = (i + 1, franja.Nombre, franja.HoraInicio);
        }
    }

    private static DateOnly Hoy() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HoraUruguay));
}

public class AsignacionDuplicadaException(string mensaje) : Exception(mensaje);

public class EstadoDeRutaException(string mensaje) : Exception(mensaje);
