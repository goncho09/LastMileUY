using LastMileUY.API.Modules.Identidad.Application;
using LastMileUY.Contracts.Usuarios;

namespace LastMileUY.API.Modules.Identidad.Endpoints;

public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosEndpoints(
        this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios")
            .WithTags("Usuarios")
            .RequireAuthorization(politica =>
                politica.RequireRole("Operador"));

        grupo.MapGet("/", async (
            GestionUsuariosService servicio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var usuarios = await servicio.ListarAsync(
                    cancellationToken);

                return Results.Ok(usuarios);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });

        grupo.MapPost("/", async (
            CrearUsuarioRequest request,
            GestionUsuariosService servicio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (usuario, error) = await servicio.CrearAsync(
                    request,
                    cancellationToken);

                if (error is not null)
                {
                    return Results.BadRequest(new
                    {
                        mensaje = error
                    });
                }

                return Results.Created(
                    $"/api/usuarios/{usuario!.Id}",
                    usuario);
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });


        grupo.MapPatch("/{id}/estado", async (
            string id,
            CambiarEstadoUsuarioRequest request,
            GestionUsuariosService servicio,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (exito, error) = await servicio.CambiarEstadoAsync(
                    id,
                    request.Activo,
                    cancellationToken);

                if (!exito)
                {
                    return Results.BadRequest(new { mensaje = error });
                }

                return Results.NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
        });


        return app;
    }
}