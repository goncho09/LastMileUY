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

public class SincronizarEntregasService(
    ApplicationDbContext db,
    IContextoOperador contextoOperador,
    IAlmacenEvidencias almacen,
    ILogger<SincronizarEntregasService> logger)
{
    public const int MaximoOperaciones = 10;

    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromMinutes(5);

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

        foreach (var operacion in operaciones)
        {
            try
            {
                resultados.Add(await ProcesarAsync(operacion, usuarioId, operadorId, cancellationToken));
            }
            catch (Exception ex) when (ex is DbUpdateException or IOException)
            {
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
        db.ChangeTracker.Clear();

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

        var anterior = await db.OperacionesSincronizadas.AsNoTracking()
            .FirstOrDefaultAsync(o => o.IdOperacion == operacion.IdOperacion, cancellationToken);

        if (anterior is not null)
        {
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
            db.ChangeTracker.Clear();
            (resultado, mensaje, intento) = Conflicto(ex.Message);
        }

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

        var bultosPorEntregar = envio.Bultos
            .Where(b => b.Estado is EstadoBulto.Pendiente
                or EstadoBulto.NoEntregado)
            .ToList();

        if (bultosPorEntregar.Count == 0)
        {
            return Conflicto(
                "El envío no tiene bultos pendientes de entrega.");
        }

        if (tipo == TipoOperacion.EntregaParcial)
        {
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

            if (entregadosAnteriormente + entregadosAhora == envio.Bultos.Count)
            {
                return Rechazada(
                    "Todos los bultos quedarían entregados. Usá Entrega.");
            }

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

        if (envio.Estado == EstadoEnvio.EntregadoParcialmente && ruta.ReglaEntregaParcial is { } reglaDeLaRuta)
        {
            envio.AplicarPoliticaBultosPendientes(reglaDeLaRuta, regla, ocurrido: fecha);

            mensaje += envio.Estado == EstadoEnvio.Reprogramado
                ? " Los bultos pendientes vuelven a salir en otra hoja de ruta."
                : " Los bultos pendientes vuelven al comercio.";
        }

        intento.ParadaId = parada.Id;

        parada.FechaLlegada ??= fecha;

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
