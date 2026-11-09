using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.API.Modules.Destinatarios.Domain;
using LastMileUY.Contracts.Destinatarios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Destinatarios.Application;

// Alta de destinatarios y de sus direcciones de entrega. Quién puede: los mismos que dan de alta
// envíos (Comercio y Operador). El usuario de un comercio solo carga destinatarios de su comercio;
// el operador, de los comercios con los que trabaja. El filtro de EF y el RLS de "Destinatarios" y
// "Direcciones" lo vuelven a controlar en la base.
public class AltaDestinatariosService(ApplicationDbContext db, IContextoOperador contextoOperador)
{
    public async Task<DestinatarioDto> CrearAsync(
        CrearDestinatarioRequest request,
        CancellationToken cancellationToken = default)
    {
        var operadorId = contextoOperador.OperadorId
            ?? throw new UnauthorizedAccessException("No se pudo identificar el operador autenticado.");

        // El usuario de un comercio no elige: es el suyo. Si manda otro, no puede.
        var comercioId = request.ComercioId;
        if (contextoOperador.ComercioId is { } propio)
        {
            if (comercioId != 0 && comercioId != propio)
            {
                throw new UnauthorizedAccessException("No podés cargar destinatarios de otro comercio.");
            }

            comercioId = propio;
        }
        else if (comercioId <= 0)
        {
            throw new ArgumentException("Elegí el comercio del destinatario.");
        }

        var nombre = Obligatorio(request.Nombre, "el nombre", 100);
        var apellido = Obligatorio(request.Apellido, "el apellido", 100);
        var email = Opcional(request.Email, "el email", 250)?.ToLowerInvariant();
        var telefono = Opcional(request.Telefono, "el teléfono", 30);

        if (email is not null && !new EmailAddressAttribute().IsValid(email))
        {
            throw new ArgumentException("El email no es válido.");
        }

        var direccion = ArmarDireccion(request.Direccion
            ?? throw new ArgumentException("El destinatario tiene que tener una dirección de entrega."));

        // Igual que al crear un envío: el comercio tiene que estar activo y trabajar con este operador
        var comercioValido = await db.Set<OperadorComercio>()
            .AnyAsync(oc =>
                    oc.OperadorId == operadorId &&
                    oc.ComercioId == comercioId &&
                    oc.Activo &&
                    db.Comercios.Any(c => c.Id == oc.ComercioId && c.Activo),
                cancellationToken);

        if (!comercioValido)
        {
            throw new ArgumentException("El comercio no está activo o no trabaja con este operador.");
        }

        var destinatario = new Destinatario
        {
            ComercioId = comercioId,
            Nombre = nombre,
            Apellido = apellido,
            Email = email,
            Telefono = telefono,
            Direcciones = [direccion]
        };

        db.Destinatarios.Add(destinatario);
        await db.SaveChangesAsync(cancellationToken);

        return new DestinatarioDto(
            destinatario.Id,
            destinatario.ComercioId,
            destinatario.Nombre,
            destinatario.Apellido,
            destinatario.Email,
            destinatario.Telefono,
            [ADto(direccion)]);
    }

    // Otra dirección para un destinatario que ya existe. Si no lo ve (es de otro comercio u
    // operador), es como si no existiera: null.
    public async Task<DireccionDto?> AgregarDireccionAsync(
        int destinatarioId,
        CrearDireccionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (contextoOperador.OperadorId is null)
        {
            throw new UnauthorizedAccessException("No se pudo identificar el operador autenticado.");
        }

        var direccion = ArmarDireccion(request);

        var existe = await db.Destinatarios.AnyAsync(d => d.Id == destinatarioId, cancellationToken);
        if (!existe)
        {
            return null;
        }

        direccion.DestinatarioId = destinatarioId;
        db.Direcciones.Add(direccion);
        await db.SaveChangesAsync(cancellationToken);

        return ADto(direccion);
    }

    private static Direccion ArmarDireccion(CrearDireccionRequest request)
    {
        if (request.Latitud is null != request.Longitud is null)
        {
            throw new ArgumentException("La ubicación lleva latitud y longitud, o ninguna de las dos.");
        }

        if (request.Latitud is < -90 or > 90 || request.Longitud is < -180 or > 180)
        {
            throw new ArgumentException("La latitud va entre -90 y 90, y la longitud entre -180 y 180.");
        }

        return new Direccion
        {
            Calle = Obligatorio(request.Calle, "la calle", 150),
            Numero = Obligatorio(request.Numero, "el número de puerta", 20),
            Apartamento = Opcional(request.Apartamento, "el apartamento", 50),
            Ciudad = Obligatorio(request.Ciudad, "la ciudad", 100),
            Departamento = DepartamentoValido(request.Departamento),
            CodigoPostal = Opcional(request.CodigoPostal, "el código postal", 20),
            Referencia = Opcional(request.Referencia, "la referencia", 300),
            // La base guarda 6 decimales (unos 10 cm)
            Latitud = request.Latitud is { } lat ? Math.Round(lat, 6) : null,
            Longitud = request.Longitud is { } lon ? Math.Round(lon, 6) : null,
            Activa = true
        };
    }

    private static string DepartamentoValido(string? valor)
    {
        var buscado = SinTildes(valor?.Trim() ?? string.Empty);

        // Se aceptan sin tildes y en cualquier mayúscula; se guarda como está en la lista
        return DepartamentosUruguay.Todos.FirstOrDefault(d =>
                   string.Equals(SinTildes(d), buscado, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException("El departamento tiene que ser uno de los 19 de Uruguay.");
    }

    private static string SinTildes(string texto) =>
        new(texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

    private static string Obligatorio(string? valor, string campo, int maximo)
    {
        var limpio = valor?.Trim();

        if (string.IsNullOrEmpty(limpio))
        {
            throw new ArgumentException($"Completá {campo}.");
        }

        return limpio.Length <= maximo
            ? limpio
            : throw new ArgumentException($"{char.ToUpperInvariant(campo[0])}{campo[1..]} admite como máximo {maximo} caracteres.");
    }

    private static string? Opcional(string? valor, string campo, int maximo)
    {
        var limpio = valor?.Trim();

        if (string.IsNullOrEmpty(limpio))
        {
            return null;
        }

        return limpio.Length <= maximo
            ? limpio
            : throw new ArgumentException($"{char.ToUpperInvariant(campo[0])}{campo[1..]} admite como máximo {maximo} caracteres.");
    }

    private static DireccionDto ADto(Direccion d) => new(
        d.Id, d.Calle, d.Numero, d.Apartamento, d.Ciudad, d.Departamento, d.CodigoPostal, d.Referencia,
        d.Latitud, d.Longitud);
}
