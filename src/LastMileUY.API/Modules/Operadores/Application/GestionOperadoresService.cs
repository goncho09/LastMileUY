using LastMileUY.Contracts.Administracion;
using LastMileUY.API.Modules.Identidad.Application;

using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.Contracts.Operadores;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Operadores.Application;

public sealed class GestionOperadoresService(
    ApplicationDbContext db, AltaUsuarioInicialService? altaUsuario = null)
{
    public async Task<List<OperadorAdminDto>> ListarAsync(
        CancellationToken ct)
    {
        return await db.Operadores
            .AsNoTracking()
            .OrderBy(o => o.Nombre)
            .Select(o => new OperadorAdminDto(
                o.Id, o.Nombre, o.Activo,
                o.FechaCreacion, o.ColorPrimario, o.LogoUrl))
            .ToListAsync(ct);
    }

    public async Task<OperadorAdminDto?> ObtenerAsync(
        int id, CancellationToken ct)
    {
        return await db.Operadores
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new OperadorAdminDto(
                o.Id, o.Nombre, o.Activo,
                o.FechaCreacion, o.ColorPrimario, o.LogoUrl))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OperadorAdminDto> CrearConUsuarioAsync(
        CrearOperadorConUsuarioRequest request, CancellationToken ct)
    {
        var datos = new GuardarOperadorAdminRequest(request.Nombre,
            request.ColorPrimario, request.LogoUrl);
        Validar(datos);
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var operador = new Operador
        {
            Nombre = datos.Nombre.Trim(),
            ColorPrimario = Limpiar(datos.ColorPrimario),
            LogoUrl = Limpiar(datos.LogoUrl)
        };
        db.Operadores.Add(operador);
        await db.SaveChangesAsync(ct);
        await (altaUsuario ?? throw new InvalidOperationException("Servicio de alta no configurado.")).CrearAsync(request.Usuario, "Operador", operador.Id, null);
        await transaccion.CommitAsync(ct);
        return Convertir(operador);
    }

    // Compatibilidad para pruebas y flujos internos; el endpoint público exige usuario inicial.
    public async Task<OperadorAdminDto> CrearAsync(GuardarOperadorAdminRequest request, CancellationToken ct)
    {
        Validar(request);
        var operador = new Operador
        {
            Nombre = request.Nombre.Trim(),
            ColorPrimario = Limpiar(request.ColorPrimario),
            LogoUrl = Limpiar(request.LogoUrl)
        };
        db.Operadores.Add(operador);
        await db.SaveChangesAsync(ct);
        return Convertir(operador);
    }

    public async Task<bool> EditarAsync(
        int id,
        GuardarOperadorAdminRequest request,
        CancellationToken ct)
    {
        Validar(request);

        var operador = await db.Operadores
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (operador is null)
            return false;

        operador.Nombre = request.Nombre.Trim();
        operador.ColorPrimario = Limpiar(request.ColorPrimario);
        operador.LogoUrl = Limpiar(request.LogoUrl);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Validar(GuardarOperadorAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre) ||
            request.Nombre.Trim().Length > 150)
            throw new ArgumentException(
                "El nombre debe tener entre 1 y 150 caracteres.");

        var color = Limpiar(request.ColorPrimario);
        if (color is not null &&
            !System.Text.RegularExpressions.Regex.IsMatch(
                color, "^#[0-9A-Fa-f]{6}$"))
            throw new ArgumentException(
                "El color debe tener formato #RRGGBB.");

        var logo = Limpiar(request.LogoUrl);
        if (logo is not null &&
            (logo.Length > 500 ||
             !Uri.TryCreate(logo, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp &&
              uri.Scheme != Uri.UriSchemeHttps)))
            throw new ArgumentException(
                "El logo debe ser una URL HTTP o HTTPS válida.");
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static OperadorAdminDto Convertir(Operador o) =>
        new(o.Id, o.Nombre, o.Activo,
            o.FechaCreacion, o.ColorPrimario, o.LogoUrl);
}
