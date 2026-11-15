using System.Globalization;
using System.Text;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Modules.Reportes.Application;

namespace LastMileUY.API.Modules.Reportes.Endpoints;

public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/reportes").WithTags("Reportes");

        grupo.MapGet("/", async (DateOnly desde, DateOnly hasta, ReportesService reportes, CancellationToken ct) =>
                await Ejecutar(async () => Results.Ok(await reportes.ObtenerAsync(desde, hasta, null, ct))))
            .RequireAuthorization(p => p.RequireRole("Operador", "Despachador"))
            .WithName("ReporteDeGestion");

        grupo.MapGet("/liquidacion.csv", async (DateOnly desde, DateOnly hasta, ReportesService reportes, CancellationToken ct) =>
                await Ejecutar(async () =>
                {
                    var reporte = await reportes.ObtenerAsync(desde, hasta, null, ct);
                    var csv = new StringBuilder("comercio;envios;entregados;devueltos;sin_tarifa;importe\n");

                    foreach (var l in reporte.Liquidacion)
                    {
                        csv.Append(Celda(l.Comercio)).Append(';').Append(l.Envios).Append(';').Append(l.Entregados).Append(';')
                            .Append(l.Devueltos).Append(';').Append(l.SinTarifa).Append(';')
                            .Append(l.Importe.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
                    }

                    return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
                        "text/csv; charset=utf-8", $"liquidacion-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.csv");
                }))
            .RequireAuthorization(p => p.RequireRole("Operador", "Despachador"))
            .WithName("LiquidacionCsv");

        grupo.MapGet("/mi-cuenta", async (
                DateOnly desde, DateOnly hasta, ReportesService reportes, IContextoOperador contexto, CancellationToken ct) =>
            {
                if (contexto.ComercioId is not { } comercioId)
                    return Results.Forbid();

                return await Ejecutar(async () => Results.Ok(await reportes.ObtenerAsync(desde, hasta, comercioId, ct)));
            })
            .RequireAuthorization(p => p.RequireRole("Comercio"))
            .WithName("CuentaDelComercio");

        return app;
    }

    private static string Celda(string valor) =>
        valor.IndexOfAny([';', '"', '\n']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;

    private static async Task<IResult> Ejecutar(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { mensaje = ex.Message });
        }
    }
}
