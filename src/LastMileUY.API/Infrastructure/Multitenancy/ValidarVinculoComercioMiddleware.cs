
using System.Security.Claims;
using LastMileUY.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Infrastructure.Multitenancy;

public sealed class ValidarVinculoComercioMiddleware(
    RequestDelegate siguiente)
{
    public async Task InvokeAsync(
        HttpContext contexto,
        ApplicationDbContext db)
    {
        if (contexto.User.Identity?.IsAuthenticated != true)
        {
            await siguiente(contexto);
            return;
        }

        if (contexto.Request.Path.StartsWithSegments("/api/auth") &&
            (contexto.Request.Path.Value?.EndsWith(
                 "/logout", StringComparison.OrdinalIgnoreCase) == true ||
             contexto.Request.Path.Value?.EndsWith(
                 "/login", StringComparison.OrdinalIgnoreCase) == true))
        {
            await siguiente(contexto);
            return;
        }

        var comercioClaim = contexto.User.FindFirstValue(
            ContextoOperadorHttp.ClaimComercioId);

        if (comercioClaim is null)
        {
            await siguiente(contexto);
            return;
        }

        var operadorClaim = contexto.User.FindFirstValue(
            ContextoOperadorHttp.ClaimOperadorId);

        if (!int.TryParse(comercioClaim, out var comercioId) ||
            !int.TryParse(operadorClaim, out var operadorId) ||
            comercioId <= 0 || operadorId <= 0)
        {
            contexto.Response.StatusCode =
                StatusCodes.Status403Forbidden;
            return;
        }

        var accesoValido = await db.OperadoresComercios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(v =>
                    v.ComercioId == comercioId &&
                    v.OperadorId == operadorId &&
                    v.Activo &&
                    db.Comercios.IgnoreQueryFilters().Any(c =>
                        c.Id == v.ComercioId && c.Activo) &&
                    db.Operadores.IgnoreQueryFilters().Any(o =>
                        o.Id == v.OperadorId && o.Activo),
                contexto.RequestAborted);

        if (!accesoValido)
        {
            contexto.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            await contexto.Response.WriteAsJsonAsync(new
            {
                mensaje = "El comercio ya no tiene acceso a este operador."
            });

            return;
        }

        await siguiente(contexto);
    }
}
