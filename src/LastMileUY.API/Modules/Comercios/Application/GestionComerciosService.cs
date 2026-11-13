using LastMileUY.Contracts.Administracion;
using LastMileUY.API.Modules.Identidad.Application;

using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Comercios.Domain;
using LastMileUY.Contracts.Comercios;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Comercios.Application;

public sealed class GestionComerciosService(ApplicationDbContext db, AltaUsuarioInicialService? altaUsuario = null)
{

    public Task<List<ComercioAdminDto>> ListarAsync(
        CancellationToken ct) =>
        db.Comercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(c => c.Nombre)
            .Select(c => new ComercioAdminDto(
                c.Id, c.Nombre, c.RazonSocial, c.RUT,
                c.Email, c.Telefono, c.Activo, c.FechaCreacion))
            .ToListAsync(ct);

    public Task<ComercioAdminDto?> ObtenerAsync(
        int id, CancellationToken ct) =>
        db.Comercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new ComercioAdminDto(
                c.Id, c.Nombre, c.RazonSocial, c.RUT,
                c.Email, c.Telefono, c.Activo, c.FechaCreacion))
            .FirstOrDefaultAsync(ct);

    public async Task<ComercioAdminDto> CrearConUsuarioAsync(
        CrearComercioConUsuarioRequest request, CancellationToken ct)
    {
        var datos = new GuardarComercioAdminRequest(request.Nombre,
            request.RazonSocial, request.RUT, request.Email, request.Telefono);
        Validar(datos);
        await ValidarRutUnicoAsync(datos.RUT, null, ct);
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var comercio = new Comercio();
        Aplicar(comercio, datos);
        db.Comercios.Add(comercio);
        await db.SaveChangesAsync(ct);
        await (altaUsuario ?? throw new InvalidOperationException("Servicio de alta no configurado.")).CrearAsync(request.Usuario, "Comercio", null, comercio.Id);
        await transaccion.CommitAsync(ct);
        return Convertir(comercio);
    }

    public async Task<ComercioAdminDto> CrearAsync(GuardarComercioAdminRequest request, CancellationToken ct)
    {
        Validar(request);
        await ValidarRutUnicoAsync(request.RUT, null, ct);
        var comercio = new Comercio();
        Aplicar(comercio, request);
        db.Comercios.Add(comercio);
        await db.SaveChangesAsync(ct);
        return Convertir(comercio);
    }

    public async Task<bool> EditarAsync(
        int id, GuardarComercioAdminRequest request,
        CancellationToken ct)
    {
        Validar(request);

        var comercio = await db.Comercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (comercio is null)
            return false;

        await ValidarRutUnicoAsync(request.RUT, id, ct);

        Aplicar(comercio, request);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<List<VinculoOperadorComercioDto>> ListarVinculosAsync(
        int comercioId, CancellationToken ct) =>
        (from vinculo in db.OperadoresComercios
                .IgnoreQueryFilters()
                .AsNoTracking()
         join operador in db.Operadores
                 .IgnoreQueryFilters()
                 .AsNoTracking()
             on vinculo.OperadorId equals operador.Id
         where vinculo.ComercioId == comercioId && !operador.EsAmbientePruebas
         orderby operador.Nombre
         select new VinculoOperadorComercioDto(
             operador.Id, operador.Nombre, vinculo.Activo,
             vinculo.FechaVinculacion))
        .ToListAsync(ct);

    public async Task<bool> VincularAsync(
        int comercioId, int operadorId, CancellationToken ct)
    {
        var existeComercio = await db.Comercios
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == comercioId, ct);

        var existeOperador = await db.Operadores
            .IgnoreQueryFilters()
            .AnyAsync(o => o.Id == operadorId && !o.EsAmbientePruebas, ct);

        if (!existeComercio || !existeOperador)
            return false;

        var vinculo = await db.OperadoresComercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                v => v.ComercioId == comercioId &&
                     v.OperadorId == operadorId, ct);

        if (vinculo is not null)
        {
            if (!vinculo.Activo)
            {
                vinculo.Activo = true;
                await db.SaveChangesAsync(ct);
            }

            return true;
        }

        db.OperadoresComercios.Add(new OperadorComercio
        {
            ComercioId = comercioId,
            OperadorId = operadorId
        });

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DesvincularAsync(
        int comercioId, int operadorId, CancellationToken ct)
    {
        if (await db.Operadores.AnyAsync(o => o.Id == operadorId && o.EsAmbientePruebas, ct))
            return false;

        var vinculo = await db.OperadoresComercios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                v => v.ComercioId == comercioId &&
                     v.OperadorId == operadorId, ct);

        if (vinculo is null)
            return false;

        vinculo.Activo = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ValidarRutUnicoAsync(
        string? rut, int? excluirId, CancellationToken ct)
    {
        var limpio = Limpiar(rut);
        if (limpio is null)
            return;

        var duplicado = await db.Comercios
            .IgnoreQueryFilters()
            .AnyAsync(
                c => c.RUT == limpio &&
                     (!excluirId.HasValue || c.Id != excluirId.Value), ct);

        if (duplicado)
            throw new ArgumentException(
                "Ya existe un comercio con ese RUT.");
    }

    private static void Validar(GuardarComercioAdminRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre) ||
            r.Nombre.Trim().Length > 150)
            throw new ArgumentException(
                "El nombre debe tener entre 1 y 150 caracteres.");

        if (Limpiar(r.RazonSocial)?.Length > 200 ||
            Limpiar(r.RUT)?.Length > 20 ||
            Limpiar(r.Email)?.Length > 250 ||
            Limpiar(r.Telefono)?.Length > 30)
            throw new ArgumentException(
                "Uno de los campos supera la longitud permitida.");

        if (Limpiar(r.Email) is string email &&
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute()
                .IsValid(email))
            throw new ArgumentException("El email no es válido.");
    }

    private static void Aplicar(
        Comercio c, GuardarComercioAdminRequest r)
    {
        c.Nombre = r.Nombre.Trim();
        c.RazonSocial = Limpiar(r.RazonSocial);
        c.RUT = Limpiar(r.RUT);
        c.Email = Limpiar(r.Email);
        c.Telefono = Limpiar(r.Telefono);
    }

    private static string? Limpiar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static ComercioAdminDto Convertir(Comercio c) =>
        new(c.Id, c.Nombre, c.RazonSocial, c.RUT,
            c.Email, c.Telefono, c.Activo, c.FechaCreacion);
}
