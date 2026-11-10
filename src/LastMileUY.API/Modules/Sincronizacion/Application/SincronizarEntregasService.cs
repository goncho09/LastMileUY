using System.Security.Cryptography;
using System.Text;
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
//   Lo mismo bulto por bulto: si alguno no está como lo vio el celular (CompararBultos).
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
            return SinRegistrar(
                operacion,
                $"El tipo '{operacion.Tipo}' no existe: tiene que ser Entrega, IntentoFallido o EntregaParcial.");
        }

        var huella = Huella(operacion, tipo);

        // ¿Ya llegó antes? Se contesta lo mismo que la primera vez, sin volver a aplicarla
        var anterior = await db.OperacionesSincronizadas.AsNoTracking()
            .FirstOrDefaultAsync(o => o.IdOperacion == operacion.IdOperacion, cancellationToken);

        if (anterior is not null)
        {
            // Un reenvío trae exactamente lo mismo. Si trae otra cosa (otro envío, otro motivo u otro
            // resultado para algún bulto) no es un reenvío: no se aplica ni pisa lo que ya quedó.
            if (anterior.Huella is not null && anterior.Huella != huella)
            {
                return SinRegistrar(
                    operacion,
                    "Ya llegó otra operación con este IdOperacion y con otro contenido: esta no se aplicó. " +
                    "Una operación nueva tiene que llevar un IdOperacion nuevo.");
            }

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

        // Queda anotado con qué versión de las reglas de entrega parcial se evaluó (la de la hoja de ruta)
        var reglaEntregaParcialId = await db.Rutas.AsNoTracking()
            .Where(r => r.Id == operacion.RutaId)
            .Select(r => r.ReglaEntregaParcialId)
            .FirstOrDefaultAsync(cancellationToken);

        db.OperacionesSincronizadas.Add(new OperacionSincronizada
        {
            IdOperacion = operacion.IdOperacion,
            Huella = huella,
            UsuarioId = usuarioId,
            Tipo = tipo,
            RutaId = operacion.RutaId,
            EnvioId = operacion.EnvioId,
            Resultado = resultado,
            Mensaje = mensaje,
            FechaHoraDispositivo = fecha,
            IntentoEntrega = intento,
            ReglaEntregaParcialId = reglaEntregaParcialId
        });

        // El cambio del envío, la ruta, el intento con su prueba y el registro de la operación van juntos
        await db.SaveChangesAsync(cancellationToken);

        TelemetriaSincronizacion.Registrar(resultado, fecha);

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
            return Rechazada(
                "La fecha del dispositivo está en el futuro: revisá la hora del celular.");
        }

        if (operacion.Latitud is < -90 or > 90 ||
            operacion.Longitud is < -180 or > 180)
        {
            return Rechazada("La ubicación no es válida.");
        }

        if (operacion.Observaciones?.Length > 1000
            || operacion.NombreReceptor?.Length > 150
            || operacion.DocumentoReceptor?.Length > 50)
        {
            return Rechazada(
                "Las observaciones, el nombre o el documento de quien recibe son demasiado largos.");
        }

        // Comprobar que la ruta existe y pertenece al repartidor.
        var ruta = await db.Rutas
            .Include(r => r.Paradas)
            .Include(r => r.ReglaEntregaParcial)
            .FirstOrDefaultAsync(
                r => r.Id == operacion.RutaId,
                cancellationToken);

        if (ruta is null)
            return Rechazada("La ruta no existe.");

        var duenio = await db.Repartidores
            .Where(r => r.Id == ruta.RepartidorId)
            .Select(r => r.UsuarioId)
            .FirstAsync(cancellationToken);

        if (duenio != usuarioId)
            return Rechazada("La ruta es de otro repartidor.");

        var parada = ruta.Paradas
            .FirstOrDefault(p => p.EnvioId == operacion.EnvioId);

        if (parada is null)
            return Rechazada("El envío no está en esta ruta.");

        if (!Ruta.EstadosParaElRepartidor.Contains(ruta.Estado))
        {
            return Conflicto(
                $"La ruta está {ruta.Estado}: ya no recibe entregas.");
        }

        // Cargar el envío con sus bultos e intentos anteriores.
        var envio = await db.Envios
            .Include(e => e.IntentosEntrega)
            .Include(e => e.Bultos)
            .FirstOrDefaultAsync(
                e => e.Id == operacion.EnvioId,
                cancellationToken);

        if (envio is null)
            return Rechazada("El envío no existe o no está disponible.");

        if (envio.Estado != EstadoEnvio.EnTransito)
        {
            return Conflicto(
                $"El envío ya está {envio.Estado} en el servidor: " +
                "no se aplicó. Lo resuelve el despachador.");
        }

        var regla = await db.ObtenerVigenteAsync(
            envio.Modalidad,
            cancellationToken);

        var ubicacion = (operacion.Latitud, operacion.Longitud);

        // Solo los bultos que todavía requieren entrega.
        var bultosPorEntregar = envio.Bultos
            .Where(b => b.Estado is EstadoBulto.Pendiente
                or EstadoBulto.NoEntregado)
            .ToList();

        if (bultosPorEntregar.Count == 0)
        {
            return Conflicto(
                "El envío no tiene bultos pendientes de entrega.");
        }

        // Validar los resultados enviados por la PWA.
        if (tipo == TipoOperacion.EntregaParcial)
        {
            // Vale la versión con la que salió la hoja de ruta, no la que esté vigente al sincronizar:
            // el repartidor trabajó con esas reglas, aunque el operador haya publicado otras después.
            // Sin versión (la ruta salió antes de que el operador publicara alguna) se permite, como antes.
            if (ruta.ReglaEntregaParcial is { PermiteEntregaParcial: false } reglaParcial)
            {
                return Rechazada(
                    $"La hoja de ruta salió con la versión {reglaParcial.Version} de las reglas de entrega parcial, " +
                    "que no permite entregas parciales.");
            }

            if (operacion.ResultadosBultos is null || operacion.ResultadosBultos.Count == 0)
            {
                return Rechazada(
                    "La entrega parcial requiere resultados de bultos.");
            }
        }

        // Lo que el celular dice de cada bulto, contra lo que tiene el servidor (docs/sincronizacion.md).
        // Una entrega sin la lista (la mandaba la app antes de conocer los bultos) entrega los que falten.
        List<ResultadoBultoRequest>? resultadosAAplicar = null;

        if (tipo is TipoOperacion.Entrega or TipoOperacion.EntregaParcial
            && operacion.ResultadosBultos is { Count: > 0 } informados)
        {
            var ids = informados.Select(r => r.BultoId).ToList();

            if (ids.Distinct().Count() != ids.Count)
                return Rechazada("Hay bultos duplicados.");

            if (ids.Any(id => envio.Bultos.All(b => b.Id != id)))
            {
                return Rechazada(
                    "La operación contiene bultos que no corresponden a esta entrega.");
            }

            if (informados.Any(r =>
                    (r.Entregado && r.MotivoNoEntregaId is not null) ||
                    (!r.Entregado && r.MotivoNoEntregaId is null)))
            {
                return Rechazada(
                    "Los motivos de no entrega de los bultos no son válidos.");
            }

            if (tipo == TipoOperacion.Entrega && informados.Any(r => !r.Entregado))
            {
                return Rechazada(
                    "En una entrega todos los bultos van como entregados. Usá EntregaParcial.");
            }

            var (aplicar, conflictos) = CompararBultos(envio, informados);

            if (conflictos.Count > 0)
            {
                return Conflicto(
                    "No se aplicó: los bultos no están como los vio el celular. " +
                    string.Join(" ", conflictos) +
                    " Lo resuelve el despachador.");
            }

            resultadosAAplicar = aplicar;
        }

        if (tipo == TipoOperacion.EntregaParcial)
        {
            var resultados = resultadosAAplicar!;

            var motivosIds = resultados
                .Where(r => !r.Entregado)
                .Select(r => r.MotivoNoEntregaId!.Value)
                .Distinct()
                .ToList();

            var motivosValidos = await db.MotivosNoEntrega
                .CountAsync(
                    m => motivosIds.Contains(m.Id) && m.Activo,
                    cancellationToken);

            if (motivosValidos != motivosIds.Count)
            {
                return Rechazada(
                    "Hay motivos de no entrega inexistentes o inactivos.");
            }

            var entregadosAnteriormente = envio.Bultos.Count(
                b => b.Estado == EstadoBulto.Entregado);

            var entregadosAhora = resultados.Count(r => r.Entregado);

            // Si termina todo entregado, corresponde Entrega.
            if (entregadosAnteriormente + entregadosAhora == envio.Bultos.Count)
            {
                return Rechazada(
                    "Todos los bultos quedarían entregados. Usá Entrega.");
            }

            // Si nunca se entregó ninguno, corresponde IntentoFallido.
            if (entregadosAnteriormente == 0 && entregadosAhora == 0)
            {
                return Rechazada(
                    "No se entregó ningún bulto. Usá IntentoFallido.");
            }
        }

        IntentoEntrega intento;
        string mensaje;

        if (tipo is TipoOperacion.Entrega or TipoOperacion.EntregaParcial)
        {
            // Validar evidencia obligatoria.
            var faltante = EvidenciaFaltante(operacion, regla);

            if (faltante is not null)
                return Rechazada(faltante);

            var firma = LeerImagen(operacion.FirmaBase64);
            var foto = LeerImagen(operacion.FotoBase64);

            if (firma.Invalida || foto.Invalida)
            {
                return Rechazada(
                    $"La {(firma.Invalida ? "firma" : "foto")} " +
                    "no es una imagen PNG o JPEG de hasta 1 MB.");
            }

            var receptor = string.IsNullOrWhiteSpace(operacion.NombreReceptor)
                ? null
                : operacion.NombreReceptor.Trim();

            List<ResultadoBultoIntento> resultados;

            if (resultadosAAplicar is not null)
            {
                // Resultado individual enviado desde la PWA (sin los bultos que ya estaban así en el servidor)
                resultados = resultadosAAplicar
                    .Select(r => new ResultadoBultoIntento
                    {
                        BultoId = r.BultoId,
                        Entregado = r.Entregado,
                        MotivoNoEntregaId = r.MotivoNoEntregaId
                    })
                    .ToList();
            }
            else
            {
                // Entrega completa de todos los bultos restantes (la app no mandó la lista).
                resultados = bultosPorEntregar
                    .Select(b => new ResultadoBultoIntento
                    {
                        BultoId = b.Id,
                        Entregado = true
                    })
                    .ToList();
            }

            intento = envio.RegistrarResultadoBultos(
                resultados,
                "Repartidor",
                usuarioId,
                regla,
                fecha,
                ubicacion);

            // La evidencia corresponde al intento actual.
            intento.PruebaEntrega = new PruebaEntrega
            {
                FirmaArchivoKey = firma.Imagen is null
                    ? null
                    : await almacen.GuardarAsync(
                        operadorId, firma.Imagen, cancellationToken),

                FotoArchivoKey = foto.Imagen is null
                    ? null
                    : await almacen.GuardarAsync(
                        operadorId, foto.Imagen, cancellationToken),

                NombreReceptor = receptor,

                DocumentoReceptor = string.IsNullOrWhiteSpace(
                    operacion.DocumentoReceptor)
                    ? null
                    : operacion.DocumentoReceptor.Trim(),

                Observaciones = operacion.Observaciones,
                Latitud = operacion.Latitud,
                Longitud = operacion.Longitud,
                FechaHoraDispositivo = fecha
            };

            mensaje = tipo == TipoOperacion.Entrega
                ? "Entrega registrada."
                : "Entrega parcial registrada.";
        }
        else
        {
            // Intento fallido: ningún bulto restante fue entregado.
            var motivo = operacion.MotivoNoEntregaId is { } motivoId
                ? await db.MotivosNoEntrega
                    .FirstOrDefaultAsync(
                        m => m.Id == motivoId && m.Activo,
                        cancellationToken)
                : null;

            if (motivo is null)
            {
                return Rechazada(
                    "Falta el motivo por el que no se pudo entregar (o no existe).");
            }

            var descripcion = string.IsNullOrWhiteSpace(operacion.Observaciones)
                ? motivo.Nombre
                : $"{motivo.Nombre}: {operacion.Observaciones.Trim()}";

            if (descripcion.Length > 1000)
                return Rechazada("Las observaciones son demasiado largas.");

            var resultados = bultosPorEntregar
                .Select(b => new ResultadoBultoIntento
                {
                    BultoId = b.Id,
                    Entregado = false,
                    MotivoNoEntregaId = motivo.Id
                })
                .ToList();

            intento = envio.RegistrarResultadoBultos(
                resultados,
                "Repartidor",
                usuarioId,
                regla,
                fecha,
                ubicacion);

            intento.MotivoNoEntregaId = motivo.Id;
            intento.Observaciones = descripcion;

            mensaje = envio.Estado == EstadoEnvio.EntregadoParcialmente
                ? "Intento fallido registrado. El envío continúa parcialmente entregado."
                : "Intento fallido registrado.";
        }

        // Si quedaron bultos sin entregar, se aplica la política de la versión con la que salió la ruta
        // (reprogramar o devolver). Sin versión, el envío queda entregado parcialmente y lo decide el despachador.
        if (envio.Estado == EstadoEnvio.EntregadoParcialmente && ruta.ReglaEntregaParcial is { } reglaDeLaRuta)
        {
            // Lo hace el sistema, no el repartidor: el evento queda con origen "Sistema" y sin responsable
            envio.AplicarPoliticaBultosPendientes(reglaDeLaRuta, regla, ocurrido: fecha);

            mensaje += envio.Estado == EstadoEnvio.Reprogramado
                ? " Los bultos pendientes vuelven a salir en otra hoja de ruta."
                : " Los bultos pendientes vuelven al comercio.";
        }

        // Asociar el intento con la parada correspondiente.
        intento.ParadaId = parada.Id;

        parada.FechaLlegada ??= fecha;

        // Registrar actividad y actualizar el estado de la ruta.
        ruta.RegistrarActividad(fecha);

        var otrosEnvios = ruta.Paradas
            .Select(p => p.EnvioId)
            .Where(id => id != envio.Id)
            .ToList();

        var estados = await db.Envios
            .Where(e => otrosEnvios.Contains(e.Id))
            .Select(e => e.Estado)
            .ToListAsync(cancellationToken);

        ruta.FinalizarSiTermino(estados.Append(envio.Estado));

        return (ResultadoOperacion.Aplicada, mensaje, intento);
    }


    // Bulto por bulto, lo que informó el celular contra lo que tiene el servidor. Manda el servidor:
    // - si el bulto todavía falta entregar (Pendiente o No entregado), se aplica lo del celular;
    // - si ya está entregado y el celular también lo entregó, ya está hecho: no se vuelve a aplicar;
    // - cualquier otra diferencia es un conflicto: el celular lo da por no entregado pero ya está
    //   entregado, el bulto está devuelto o extraviado, o falta entregar uno que el celular no informó
    //   (no estaba en su hoja). Con un conflicto no se aplica nada de la operación.
    private static (List<ResultadoBultoRequest> Aplicar, List<string> Conflictos) CompararBultos(
        Envio envio,
        IReadOnlyCollection<ResultadoBultoRequest> informados)
    {
        var aplicar = new List<ResultadoBultoRequest>();
        var conflictos = new List<string>();

        foreach (var bulto in envio.Bultos.OrderBy(b => b.Id))
        {
            var informado = informados.FirstOrDefault(r => r.BultoId == bulto.Id);
            var porEntregar = bulto.Estado is EstadoBulto.Pendiente or EstadoBulto.NoEntregado;

            if (informado is null)
            {
                if (porEntregar)
                {
                    conflictos.Add($"{bulto.Codigo}: falta entregarlo y el celular no informó qué pasó con él.");
                }

                continue;
            }

            if (porEntregar)
            {
                aplicar.Add(informado);
            }
            else if (bulto.Estado == EstadoBulto.Entregado)
            {
                if (!informado.Entregado)
                {
                    conflictos.Add($"{bulto.Codigo}: el celular lo informó como no entregado y ya está entregado.");
                }
            }
            else
            {
                conflictos.Add($"{bulto.Codigo}: está {bulto.Estado} en el servidor.");
            }
        }

        return (aplicar, conflictos);
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

    // Lo que decide la operación, en un orden fijo: los bultos ordenados por Id, para que el mismo
    // resultado dé la misma huella aunque la app los mande en otro orden. La evidencia (firma, foto,
    // receptor) no entra: no cambia qué se aplica.
    private static string Huella(OperacionRequest operacion, TipoOperacion tipo)
    {
        var bultos = (operacion.ResultadosBultos ?? [])
            .OrderBy(r => r.BultoId)
            .ThenBy(r => r.Entregado)
            .ThenBy(r => r.MotivoNoEntregaId)
            .Select(r => r.Entregado ? $"{r.BultoId}:E" : $"{r.BultoId}:N:{r.MotivoNoEntregaId}");

        var texto = string.Join(
            "|",
            tipo,
            operacion.RutaId,
            operacion.EnvioId,
            operacion.MotivoNoEntregaId?.ToString() ?? "-",
            string.Join(",", bultos));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    private static ResultadoOperacionDto SinRegistrar(OperacionRequest operacion, string mensaje) =>
        new(operacion.IdOperacion, ResultadoOperacion.Rechazada.ToString(), mensaje, false, false, null);
}
