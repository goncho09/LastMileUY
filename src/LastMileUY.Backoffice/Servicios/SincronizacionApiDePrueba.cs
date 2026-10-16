using LastMileUY.Contracts.Comun;
using LastMileUY.Contracts.Sincronizacion;

namespace LastMileUY.Backoffice.Servicios;

// Operaciones de ejemplo, una de cada resultado, para trabajar el Backoffice sin la API
// ("Api:UsarDatosDePrueba"). Las rutas y envíos son los de RutasApiDePrueba.
public class SincronizacionApiDePrueba : ISincronizacionApi
{
    // Un PNG de 1x1 píxel, para que la evidencia de prueba se vea como imagen
    private static readonly byte[] PixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static readonly DateTime Hoy = DateTime.UtcNow.Date.AddHours(13);

    private static readonly List<OperacionSincronizadaDto> Operaciones =
    [
        new(Guid.Parse("6f1c2a4e-0000-4000-8000-000000000001"), "Entrega", "Aplicada", "Entrega registrada.",
            1, 201, "K7M2Q9T4XW3P", "Lucía Méndez", Hoy, Hoy.AddMinutes(40), 1, true, false),
        new(Guid.Parse("6f1c2a4e-0000-4000-8000-000000000002"), "IntentoFallido", "Aplicada", "Intento fallido registrado.",
            1, 203, "M3N7B5V9C2XQ", "Lucía Méndez", Hoy.AddMinutes(25), Hoy.AddMinutes(40), 2, false, false),
        new(Guid.Parse("6f1c2a4e-0000-4000-8000-000000000003"), "Entrega", "Rechazada",
            "La regla \"Estándar\" pide la firma para entregar.",
            1, 205, "Q2W6E4R8T3YU", "Lucía Méndez", Hoy.AddMinutes(30), Hoy.AddMinutes(40), null, false, false),
        new(Guid.Parse("6f1c2a4e-0000-4000-8000-000000000004"), "Entrega", "Conflicto",
            "El envío ya está EnDeposito en el servidor: no se aplicó. Lo resuelve el despachador.",
            1, 202, "R4T8Y2U6P9HZ", "Lucía Méndez", Hoy.AddMinutes(35), Hoy.AddMinutes(40), null, false, false)
    ];

    public Task<PaginaDto<OperacionSincronizadaDto>> ListarAsync(FiltroOperaciones filtro, CancellationToken ct = default)
    {
        var filtradas = Operaciones
            .Where(o => filtro.RutaId is null || o.RutaId == filtro.RutaId)
            .Where(o => string.IsNullOrWhiteSpace(filtro.Resultado) || o.Resultado == filtro.Resultado)
            .OrderByDescending(o => o.FechaRecepcion)
            .ThenByDescending(o => o.FechaHoraDispositivo)
            .ToList();

        var items = filtradas
            .Skip((filtro.Pagina - 1) * filtro.TamanioPagina)
            .Take(filtro.TamanioPagina)
            .ToList();

        return Task.FromResult(new PaginaDto<OperacionSincronizadaDto>(items, filtradas.Count, filtro.Pagina, filtro.TamanioPagina));
    }

    public Task<Evidencia?> ObtenerEvidenciaAsync(int intentoEntregaId, string tipo, CancellationToken ct = default)
    {
        var operacion = Operaciones.FirstOrDefault(o => o.IntentoEntregaId == intentoEntregaId);
        var existe = tipo == "firma" ? operacion?.TieneFirma == true : tipo == "foto" && operacion?.TieneFoto == true;

        return Task.FromResult(existe ? new Evidencia(PixelPng, "image/png") : null);
    }
}
