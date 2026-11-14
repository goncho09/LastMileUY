using System.Security.Claims;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Integraciones.Application;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Tarifas.Application;
using LastMileUY.Contracts.Envios;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LastMileUY.API.Modules.Envios.Application;

public class CrearEnvioService(
    ApplicationDbContext db,
    IContextoOperador contextoOperador,
    OpcionesEntregaService opcionesEntrega,
    TarificadorEnvios tarificador)
{
    public static CanalIngreso CanalDe(ClaimsPrincipal usuario) =>
        usuario.HasClaim(c => c.Type == AutenticacionClaveApi.ClaimClaveApiId) ? CanalIngreso.Api
        : usuario.IsInRole("Comercio") ? CanalIngreso.Portal
        : CanalIngreso.Api;

    public async Task<EnvioResponse> EjecutarAsync(
        CrearEnvioRequest request,
        ClaimsPrincipal usuario,
        CancellationToken cancellationToken = default)
    {
        var responsableId = usuario.FindFirstValue(ClaimTypes.NameIdentifier);
        var canal = CanalDe(usuario);
        var operadorId = contextoOperador.OperadorId
            ?? throw new UnauthorizedAccessException(
                "No se pudo identificar el operador autenticado.");

        var comercioIdUsuario = contextoOperador.ComercioId;

        if (usuario.IsInRole("Comercio"))
        {
            if (!comercioIdUsuario.HasValue)
            {
                throw new UnauthorizedAccessException(
                    "El usuario de comercio no tiene un comercio asignado.");
            }

            if (comercioIdUsuario.Value != request.ComercioId)
            {
                throw new UnauthorizedAccessException(
                    "No tenés permisos para crear envíos de otro comercio.");
            }
        }

        if (request.DestinatarioId <= 0 || request.DireccionId <= 0)
        {
            throw new ArgumentException(
                "Los identificadores deben ser mayores a cero.");
        }

        ValidarBultos(request.Bultos);

        var referencia = string.IsNullOrWhiteSpace(request.ReferenciaExterna) ? null : request.ReferenciaExterna.Trim();

        if (referencia?.Length > 100)
        {
            throw new ArgumentException("La referencia externa no puede superar los 100 caracteres.");
        }

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

        var modalidad = string.IsNullOrWhiteSpace(request.Modalidad)
            ? ModalidadEnvio.Estandar
            : ConfiguracionComun.Modalidad(request.Modalidad);

        var zona = await opcionesEntrega.ZonaParaAsync(direccion.Departamento, direccion.Ciudad, cancellationToken);
        var franja = await FranjaElegidaAsync(request.FranjaHorariaId, zona, cancellationToken);

        var envio = new Envio
        {
            ComercioId = request.ComercioId,
            DestinatarioId = request.DestinatarioId,
            ReferenciaExterna = referencia,
            Canal = canal,
            Modalidad = modalidad,
            ZonaId = zona?.Id,
            FranjaHorariaId = franja?.Id
        };

        envio.AsignarDireccionEntrega(new DireccionEntrega(
            direccion.Calle,
            direccion.Numero,
            direccion.Apartamento,
            direccion.Ciudad,
            direccion.Departamento,
            direccion.CodigoPostal,
            direccion.Referencia,
            direccion.Latitud,
            direccion.Longitud
        ));

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

        if (zona is not null
            && await tarificador.CotizarAsync(
                zona.Id,
                modalidad,
                envio.Bultos.Sum(b => b.PesoKg),
                TarificadorEnvios.VolumenM3(envio.Bultos),
                cancellationToken) is { } cotizacion)
        {
            envio.CostoEnvio = cotizacion.Costo;
            envio.CuadroTarifarioId = cotizacion.CuadroTarifarioId;
        }

        envio.RegistrarAdmision(canal == CanalIngreso.Portal ? "Portal" : "API", responsableId);

        db.Envios.Add(envio);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new EnvioDuplicadoException(
                $"Ya existe un envío con la referencia '{referencia}' para ese comercio.");
        }

        return new EnvioResponse
        {
            Id = envio.Id,
            CodigoSeguimiento = envio.CodigoSeguimiento,
            ComercioId = envio.ComercioId,
            DestinatarioId = envio.DestinatarioId,
            Estado = envio.Estado.ToString(),
            FechaCreacion = envio.FechaCreacion,
            Modalidad = envio.Modalidad.ToString(),
            Zona = zona?.Nombre,
            Franja = franja?.Nombre,
            CostoEnvio = envio.CostoEnvio
        };
    }

    private async Task<FranjaHoraria?> FranjaElegidaAsync(int? franjaId, Zona? zona, CancellationToken ct)
    {
        if (franjaId is null)
            return null;

        var franja = await db.FranjasHorarias.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == franjaId && f.Activa, ct)
            ?? throw new ArgumentException("La franja horaria no existe o no está activa.");

        if (zona is null)
            throw new ArgumentException(
                "La dirección no está en ninguna zona de cobertura, así que no se le puede elegir franja.");

        if (franja.ZonaId != zona.Id)
            throw new ArgumentException(
                $"La franja \"{franja.Nombre}\" no es de la zona \"{zona.Nombre}\", que es la de la dirección.");

        return franja;
    }

    private static void ValidarBultos(List<CrearBultoRequest>? bultos)
    {
        if (bultos is null || bultos.Count == 0)
        {
            throw new ArgumentException("El envío tiene que tener al menos un bulto.");
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

public class EnvioDuplicadoException(string mensaje) : Exception(mensaje);
