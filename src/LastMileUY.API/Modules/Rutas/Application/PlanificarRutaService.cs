using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.Contracts.Rutas;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Rutas.Application;

// Arma y despacha hojas de ruta del operador actual. Igual que en los envíos, no filtra
// por operador a mano: un repartidor, vehículo o envío de otro operador "no existe" (EF + RLS).
// Reglas de armado y orden de las paradas: docs/hojas-de-ruta.md
public class PlanificarRutaService(ApplicationDbContext db)
{
    public const int MaximoParadas = 100;

    // Solo desde estos estados un envío puede pasar a AsignadoARuta (tabla de transiciones)
    private static readonly EstadoEnvio[] EstadosAsignables = [EstadoEnvio.EnDeposito, EstadoEnvio.Reprogramado];

    private static readonly TimeZoneInfo HoraUruguay = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    // Devuelve el Id de la ruta creada.
    // ArgumentException: algo del pedido no vale (400). AsignacionDuplicadaException: el repartidor,
    // el vehículo o un envío ya están en otra ruta abierta (409).
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

        var repartidor = await db.Repartidores
            .FirstOrDefaultAsync(r => r.Id == request.RepartidorId, cancellationToken)
            ?? throw new ArgumentException("El repartidor no existe.");

        if (!repartidor.Activo)
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

        // Un repartidor y un vehículo hacen una sola ruta abierta por día
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

        // Un envío no puede estar en dos rutas abiertas a la vez
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

        // El orden de la lista es el orden de visita
        for (var i = 0; i < envioIds.Count; i++)
        {
            var envio = envios[envioIds[i]];

            ruta.Paradas.Add(new Parada { EnvioId = envio.Id, Orden = i + 1 });

            envio.CambiarEstado(
                EstadoEnvio.AsignadoARuta,
                "Backoffice",
                $"Asignado a la ruta \"{nombre}\" (parada {i + 1}).",
                responsableId);
        }

        db.Rutas.Add(ruta);
        await db.SaveChangesAsync(cancellationToken);

        return ruta.Id;
    }

    // Despacha la ruta: queda disponible para el repartidor y sus envíos pasan a En tránsito.
    // Devuelve false si la ruta no existe o es de otro operador.
    // EstadoDeRutaException: la ruta no está planificada o algún envío ya no está asignado (409).
    public async Task<bool> DespacharAsync(
        int id,
        string? responsableId,
        CancellationToken cancellationToken = default)
    {
        var ruta = await db.Rutas
            .Include(r => r.Paradas)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (ruta is null)
        {
            return false;
        }

        var envioIds = ruta.Paradas.Select(p => p.EnvioId).ToList();
        var envios = await db.Envios
            .Where(e => envioIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        try
        {
            ruta.Despachar();

            // Si un envío se sacó de la ruta a mano (por ejemplo, volvió al depósito), no sale
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

        return true;
    }

    // Lo que llevan los envíos no puede pasar lo que carga el vehículo (peso y volumen)
    private static void ValidarCapacidad(Vehiculo vehiculo, IEnumerable<Envio> envios)
    {
        var bultos = envios.SelectMany(e => e.Bultos).ToList();
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

    // Las franjas tienen que estar activas y las paradas con franja ir en orden de horario:
    // no se puede visitar una franja de la mañana después de una de la tarde.
    // Las paradas sin franja pueden ir en cualquier lugar.
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

    // La fecha de la ruta se compara con el día de hoy en Uruguay, no en UTC
    private static DateOnly Hoy() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HoraUruguay));
}

public class AsignacionDuplicadaException(string mensaje) : Exception(mensaje);

public class EstadoDeRutaException(string mensaje) : Exception(mensaje);
