using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Envios.Application;

public class CrearEnvioService(
    ApplicationDbContext db,
    IContextoOperador contextoOperador)
{
    // El canal sale del perfil de quien crea el envío, nunca de lo que diga el pedido:
    // un usuario de comercio solo puede estar usando el portal.
    public static CanalIngreso CanalDe(ClaimsPrincipal usuario) =>
        usuario.IsInRole("Comercio") ? CanalIngreso.Portal : CanalIngreso.Api;

    // canal: por dónde entró el envío (ver CanalDe)
    public async Task<EnvioResponse> EjecutarAsync(
        CrearEnvioRequest request,
        string? responsableId = null,
        CanalIngreso canal = CanalIngreso.Api,
        CancellationToken cancellationToken = default)
    {
        var operadorId = contextoOperador.OperadorId
            ?? throw new UnauthorizedAccessException(
                "No se pudo identificar el operador autenticado.");

        if (request.ComercioId <= 0 ||
            request.DestinatarioId <= 0 ||
            request.DireccionId <= 0)
        {
            throw new ArgumentException(
                "Los identificadores deben ser mayores a cero.");
        }

        ValidarBultos(request.Bultos);

        var comercioValido = await db.Set<OperadorComercio>()
            .AnyAsync(oc =>
                oc.OperadorId == operadorId &&
                oc.ComercioId == request.ComercioId &&
                oc.Activo &&
                db.Comercios.Any(c =>
                    c.Id == oc.ComercioId && c.Activo),
                cancellationToken);

        if (!comercioValido)
            throw new ArgumentException(
                "El comercio no está activo o no pertenece al operador.");

        var destinatarioValido = await db.Destinatarios
            .AnyAsync(d =>
                d.Id == request.DestinatarioId &&
                d.ComercioId == request.ComercioId,
                cancellationToken);

        if (!destinatarioValido)
            throw new ArgumentException(
                "El destinatario no pertenece al comercio.");

        var direccion = await db.Direcciones
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == request.DireccionId &&
                d.DestinatarioId == request.DestinatarioId &&
                d.Activa,
                cancellationToken);

        if (direccion is null)
            throw new ArgumentException(
                "La dirección no existe, está inactiva o no pertenece al destinatario.");

        var envio = new Envio
        {
            ComercioId = request.ComercioId,
            DestinatarioId = request.DestinatarioId,
            ReferenciaExterna = request.ReferenciaExterna,
            Canal = canal
        };

        envio.AsignarDireccionEntrega(new DireccionEntrega(
            direccion.Calle,
            direccion.Numero,
            direccion.Apartamento,
            direccion.Ciudad,
            direccion.Departamento,
            direccion.CodigoPostal,
            direccion.Referencia
        ));

        // Cada bulto lleva el código del envío y su número: ABC123DEF456-1, -2...
        var numero = 1;
        foreach (var bulto in request.Bultos ?? [])
        {
            envio.Bultos.Add(new Bulto
            {
                Codigo = $"{envio.CodigoSeguimiento}-{numero++}",
                PesoKg = bulto.PesoKg,
                LargoCm = bulto.LargoCm,
                AnchoCm = bulto.AnchoCm,
                AltoCm = bulto.AltoCm,
                Descripcion = bulto.Descripcion
            });
        }

        // Primer evento del historial
        envio.RegistrarAdmision(canal == CanalIngreso.Portal ? "Portal" : "API", responsableId);

        db.Envios.Add(envio);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Índice único operador + comercio + referencia: el pedido ya se había cargado
            throw new EnvioDuplicadoException(
                $"Ya existe un envío con la referencia '{request.ReferenciaExterna}' para ese comercio.");
        }

        return new EnvioResponse
        {
            Id = envio.Id,
            CodigoSeguimiento = envio.CodigoSeguimiento,
            ComercioId = envio.ComercioId,
            DestinatarioId = envio.DestinatarioId,
            Estado = envio.Estado.ToString(),
            FechaCreacion = envio.FechaCreacion
        };
    }

    // Mismos límites que los atributos de CrearBultoRequest (las minimal APIs no los validan solas)
    private static void ValidarBultos(List<CrearBultoRequest>? bultos)
    {
        if (bultos is null)
        {
            return;
        }

        if (bultos.Count > 50)
        {
            throw new ArgumentException("Un envío no puede tener más de 50 bultos.");
        }

        foreach (var bulto in bultos)
        {
            if (bulto.PesoKg is <= 0 or > 1000 ||
                bulto.LargoCm is <= 0 or > 1000 ||
                bulto.AnchoCm is <= 0 or > 1000 ||
                bulto.AltoCm is <= 0 or > 1000)
            {
                throw new ArgumentException(
                    "El peso y las medidas de cada bulto deben ser mayores a cero y como máximo 1000.");
            }

            if (bulto.Descripcion?.Length > 500)
            {
                throw new ArgumentException("La descripción de un bulto no puede superar los 500 caracteres.");
            }
        }
    }
}

// El envío ya existía (misma referencia del comercio con el mismo operador)
public class EnvioDuplicadoException(string mensaje) : Exception(mensaje);
