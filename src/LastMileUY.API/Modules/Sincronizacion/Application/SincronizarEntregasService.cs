using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Envios.Domain;
using LastMileUY.API.Modules.Operadores.Application;
using LastMileUY.API.Modules.Operadores.Domain;
using LastMileUY.API.Modules.Rutas.Domain;
using LastMileUY.API.Modules.Sincronizacion.Domain;
using LastMileUY.API.Modules.Sincronizacion.Infrastructure;
using LastMileUY.Contracts.Sincronizacion;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Sincronizacion.Application;

// Recibe lo que la app del repartidor registró sin conexión (entregas e intentos fallidos con su
// evidencia) y lo aplica sobre los envíos. Reglas y política de conflictos: docs/sincronizacion.md
//
// - Cada operación se procesa y se guarda por separado: una que falla no frena a las demás.
// - Cada operación queda registrada con su IdOperacion. Un reenvío no se aplica dos veces:
//   devuelve el resultado de la primera vez.
// - Las transiciones y la regla operativa del operador son las mismas que en el Backoffice
//   (Envio.CambiarEstado); la evidencia obligatoria sale de la regla.
// - Conflicto: si el envío ya no está en tránsito, manda el servidor y no se aplica.
public class SincronizarEntregasService(
    ApplicationDbContext db,
    IContextoOperador contextoOperador,
    IAlmacenEvidencias almacen,
    ILogger<SincronizarEntregasService> logger)
{
    public const int MaximoOperaciones = 10;

    // Diferencia aceptada entre el reloj del celular y el del servidor
    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromMinutes(5);

    // ArgumentException: el lote viene vacío o es demasiado grande (400)
    public async Task<SincronizarResponse> EjecutarAsync(
        SincronizarRequest request,
        string usuarioId,
        CancellationToken cancellationToken = default)
    {
        var operaciones = request.Operaciones ?? [];

        if (operaciones.Count == 0)
        {
            throw new ArgumentException("No llegó ninguna operación.");
        }

        if (operaciones.Count > MaximoOperaciones)
        {
            throw new ArgumentException(
                $"Se pueden sincronizar hasta {MaximoOperaciones} operaciones por vez. Mandá el resto en otro lote.");
        }

        var operadorId = contextoOperador.OperadorId
            ?? throw new UnauthorizedAccessException("No se pudo identificar el operador.");

        var resultados = new List<ResultadoOperacionDto>();

        // En orden: si el mismo envío tiene dos operaciones, la segunda ve lo que dejó la primera
        foreach (var operacion in operaciones)
        {
            try
            {
                resultados.Add(await ProcesarAsync(operacion, usuarioId, operadorId, cancellationToken));
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException)
            {
                // Otro cambio se guardó a la vez (concurrencia, o el mismo reenvío en paralelo)
                // o no se pudo escribir la evidencia: no quedó nada aplicado, se puede reintentar.
                logger.LogWarning(ex, "No se pudo guardar la operación {IdOperacion}", operacion.IdOperacion);
                resultados.Add(new ResultadoOperacionDto(
                    operacion.IdOperacion,
                    "Error",
                    "No se pudo guardar en este momento. Volvé a sincronizar.",
                    Duplicada: false,
                    Reintentar: true,
                    EstadoEnvio: null));
            }
        }

        return new SincronizarResponse(resultados);
    }

    private async Task<ResultadoOperacionDto> ProcesarAsync(
        OperacionRequest operacion,
        string usuarioId,
        int operadorId,
        CancellationToken cancellationToken)
    {
        // Cada operación arranca de cero: lo que quedó cargado de la anterior no se mezcla
        db.ChangeTracker.Clear();

        // Sin IdOperacion o con un tipo que no existe no se puede ni registrar
        if (operacion.IdOperacion == Guid.Empty)
        {
            return SinRegistrar(operacion, "La operación no tiene IdOperacion.");
        }

        if (!Enum.TryParse<TipoOperacion>(operacion.Tipo, ignoreCase: true, out var tipo) || !Enum.IsDefined(tipo))
        {
            return SinRegistrar(operacion, $"El tipo '{operacion.Tipo}' no existe: tiene que ser Entrega o IntentoFallido.");
        }

        // ¿Ya llegó antes? Se contesta lo mismo que la primera vez, sin volver a aplicarla
        var anterior = await db.OperacionesSincronizadas.AsNoTracking()
            .FirstOrDefaultAsync(o => o.IdOperacion == operacion.IdOperacion, cancellationToken);

        if (anterior is not null)
        {
            return new ResultadoOperacionDto(
                operacion.IdOperacion,
                anterior.Resultado.ToString(),
                anterior.Mensaje,
                Duplicada: true,
                Reintentar: false,
                await EstadoDelEnvioAsync(anterior.EnvioId, cancellationToken));
        }

        var fecha = ComoUtc(operacion.FechaHoraDispositivo);
        ResultadoOperacion resultado;
        string mensaje;
        IntentoEntrega? intento;

        try
        {
            (resultado, mensaje, intento) = await AplicarAsync(operacion, tipo, fecha, usuarioId, operadorId, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // El dominio rechazó la transición (no debería pasar: antes se controla que esté en tránsito).
            // Se registra como conflicto en vez de cortar el lote, y no queda nada a medio aplicar.
            db.ChangeTracker.Clear();
            (resultado, mensaje, intento) = Conflicto(ex.Message);
        }

        db.OperacionesSincronizadas.Add(new OperacionSincronizada
        {
            IdOperacion = operacion.IdOperacion,
            UsuarioId = usuarioId,
            Tipo = tipo,
            RutaId = operacion.RutaId,
            EnvioId = operacion.EnvioId,
            Resultado = resultado,
            Mensaje = mensaje,
            FechaHoraDispositivo = fecha,
            IntentoEntrega = intento
        });

        // El cambio del envío, la ruta, el intento con su prueba y el registro de la operación van juntos
        await db.SaveChangesAsync(cancellationToken);

        return new ResultadoOperacionDto(
            operacion.IdOperacion,
            resultado.ToString(),
            mensaje,
            Duplicada: false,
            Reintentar: false,
            await EstadoDelEnvioAsync(operacion.EnvioId, cancellationToken));
    }

    private async Task<(ResultadoOperacion, string, IntentoEntrega?)> AplicarAsync(
        OperacionRequest operacion,
        TipoOperacion tipo,
        DateTime fecha,
        string usuarioId,
        int operadorId,
        CancellationToken cancellationToken)
    {
        if (fecha > DateTime.UtcNow + ToleranciaReloj)
        {
            return Rechazada("La fecha del dispositivo está en el futuro: revisá la hora del celular.");
        }

        if (operacion.Latitud is < -90 or > 90 || operacion.Longitud is < -180 or > 180)
        {
            return Rechazada("La ubicación no es válida.");
        }

        // Lo que no entra en la base no se arregla reintentando: se rechaza, así la app deja de reenviarlo
        // (mismos límites que OperacionRequest, que las minimal APIs no validan solas)
        if (operacion.Observaciones?.Length > 1000
            || operacion.NombreReceptor?.Length > 150
            || operacion.DocumentoReceptor?.Length > 50)
        {
            return Rechazada("Las observaciones, el nombre o el documento de quien recibe son demasiado largos.");
        }

        // Una ruta de otro operador no existe para este repartidor (EF + RLS)
        var ruta = await db.Rutas
            .Include(r => r.Paradas)
            .FirstOrDefaultAsync(r => r.Id == operacion.RutaId, cancellationToken);

        if (ruta is null)
        {
            return Rechazada("La ruta no existe.");
        }

        var duenio = await db.Repartidores
            .Where(r => r.Id == ruta.RepartidorId)
            .Select(r => r.UsuarioId)
            .FirstAsync(cancellationToken);

        if (duenio != usuarioId)
        {
            return Rechazada("La ruta es de otro repartidor.");
        }

        var parada = ruta.Paradas.FirstOrDefault(p => p.EnvioId == operacion.EnvioId);

        if (parada is null)
        {
            return Rechazada("El envío no está en esta ruta.");
        }

        if (!Ruta.EstadosParaElRepartidor.Contains(ruta.Estado))
        {
            return Conflicto($"La ruta está {ruta.Estado}: ya no recibe entregas.");
        }

        var envio = await db.Envios
            .Include(e => e.IntentosEntrega)
            .FirstAsync(e => e.Id == operacion.EnvioId, cancellationToken);

        // Política de conflictos: manda el servidor. Si mientras el repartidor estaba sin conexión
        // alguien cambió el envío (lo devolvió al depósito, lo marcó extraviado, otra operación
        // ya lo entregó), la operación no se aplica y lo resuelve el despachador.
        if (envio.Estado != EstadoEnvio.EnTransito)
        {
            return Conflicto(
                $"El envío ya está {envio.Estado} en el servidor: no se aplicó. Lo resuelve el despachador.");
        }

        var regla = await db.ObtenerVigenteAsync(envio.Modalidad, cancellationToken);
        var ubicacion = (operacion.Latitud, operacion.Longitud);

        IntentoEntrega intento;
        string mensaje;

        if (tipo == TipoOperacion.Entrega)
        {
            var faltante = EvidenciaFaltante(operacion, regla);

            if (faltante is not null)
            {
                return Rechazada(faltante);
            }

            var firma = LeerImagen(operacion.FirmaBase64);
            var foto = LeerImagen(operacion.FotoBase64);

            if (firma.Invalida || foto.Invalida)
            {
                return Rechazada($"La {(firma.Invalida ? "firma" : "foto")} no es una imagen PNG o JPEG de hasta 1 MB.");
            }

            var receptor = string.IsNullOrWhiteSpace(operacion.NombreReceptor) ? null : operacion.NombreReceptor.Trim();

            intento = envio.CambiarEstado(
                EstadoEnvio.Entregado,
                "Repartidor",
                receptor is null ? "Entregado." : $"Entregado a {receptor}.",
                usuarioId,
                regla,
                fecha,
                ubicacion)!;

            // Si después falla el guardado en la base, el archivo queda huérfano: no se ve en ningún lado
            intento.PruebaEntrega = new PruebaEntrega
            {
                FirmaArchivoKey = firma.Imagen is null ? null : await almacen.GuardarAsync(operadorId, firma.Imagen, cancellationToken),
                FotoArchivoKey = foto.Imagen is null ? null : await almacen.GuardarAsync(operadorId, foto.Imagen, cancellationToken),
                NombreReceptor = receptor,
                DocumentoReceptor = string.IsNullOrWhiteSpace(operacion.DocumentoReceptor) ? null : operacion.DocumentoReceptor.Trim(),
                Observaciones = operacion.Observaciones,
                Latitud = operacion.Latitud,
                Longitud = operacion.Longitud,
                FechaHoraDispositivo = fecha
            };

            mensaje = "Entrega registrada.";
        }
        else
        {
            var motivo = operacion.MotivoNoEntregaId is { } motivoId
                ? await db.MotivosNoEntrega.FirstOrDefaultAsync(m => m.Id == motivoId && m.Activo, cancellationToken)
                : null;

            if (motivo is null)
            {
                return Rechazada("Falta el motivo por el que no se pudo entregar (o no existe).");
            }

            var descripcion = string.IsNullOrWhiteSpace(operacion.Observaciones)
                ? motivo.Nombre
                : $"{motivo.Nombre}: {operacion.Observaciones.Trim()}";

            // Con el motivo adelante, el texto del historial puede pasarse aunque las observaciones entren
            if (descripcion.Length > 1000)
            {
                return Rechazada("Las observaciones son demasiado largas.");
            }

            intento = envio.CambiarEstado(
                EstadoEnvio.NoEntregado,
                "Repartidor",
                descripcion,
                usuarioId,
                regla,
                fecha,
                ubicacion)!;

            intento.MotivoNoEntregaId = motivo.Id;
            mensaje = "Intento fallido registrado.";
        }

        intento.ParadaId = parada.Id;
        parada.FechaLlegada ??= fecha;

        // La ruta pasa a En curso con la primera operación y termina cuando no le queda nada en tránsito
        ruta.RegistrarActividad(fecha);

        var otrosEnvios = ruta.Paradas.Select(p => p.EnvioId).Where(id => id != envio.Id).ToList();
        var estados = await db.Envios
            .Where(e => otrosEnvios.Contains(e.Id))
            .Select(e => e.Estado)
            .ToListAsync(cancellationToken);

        ruta.FinalizarSiTermino(estados.Append(envio.Estado));

        return (ResultadoOperacion.Aplicada, mensaje, intento);
    }

    // La regla operativa dice qué evidencia es obligatoria para entregar
    private static string? EvidenciaFaltante(OperacionRequest operacion, ReglaOperativa? regla)
    {
        if (regla is null)
        {
            return null;
        }

        var faltan = new List<string>();

        if (regla.RequiereFirma && string.IsNullOrWhiteSpace(operacion.FirmaBase64))
        {
            faltan.Add("la firma");
        }

        if (regla.RequiereFotografia && string.IsNullOrWhiteSpace(operacion.FotoBase64))
        {
            faltan.Add("una foto");
        }

        if (regla.RequiereDocumentoReceptor
            && (string.IsNullOrWhiteSpace(operacion.DocumentoReceptor) || string.IsNullOrWhiteSpace(operacion.NombreReceptor)))
        {
            faltan.Add("el nombre y el documento de quien recibe");
        }

        return faltan.Count == 0
            ? null
            : $"La regla \"{regla.Nombre}\" pide {string.Join(" y ", faltan)} para entregar.";
    }

    private static (Imagen? Imagen, bool Invalida) LeerImagen(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return (null, false);
        }

        var imagen = Imagen.DesdeBase64(base64);
        return (imagen, imagen is null);
    }

    private async Task<string?> EstadoDelEnvioAsync(int envioId, CancellationToken cancellationToken)
    {
        var estado = await db.Envios.AsNoTracking()
            .Where(e => e.Id == envioId)
            .Select(e => (EstadoEnvio?)e.Estado)
            .FirstOrDefaultAsync(cancellationToken);

        return estado?.ToString();
    }

    // Las fechas llegan en UTC; si vienen sin zona se toman como UTC (PostgreSQL guarda UTC)
    private static DateTime ComoUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };

    private static (ResultadoOperacion, string, IntentoEntrega?) Rechazada(string mensaje) =>
        (ResultadoOperacion.Rechazada, mensaje, null);

    private static (ResultadoOperacion, string, IntentoEntrega?) Conflicto(string mensaje) =>
        (ResultadoOperacion.Conflicto, mensaje, null);

    private static ResultadoOperacionDto SinRegistrar(OperacionRequest operacion, string mensaje) =>
        new(operacion.IdOperacion, ResultadoOperacion.Rechazada.ToString(), mensaje, false, false, null);
}
