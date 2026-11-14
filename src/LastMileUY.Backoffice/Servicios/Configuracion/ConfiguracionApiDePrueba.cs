using LastMileUY.Contracts.Configuracion;

namespace LastMileUY.Backoffice.Servicios;

public sealed class ConfiguracionApiDePrueba : IConfiguracionApi
{
    private readonly Lock candado = new();
    private int ultimoId = 100;

    private readonly List<ZonaDto> zonas =
    [
        new(1, "Centro", "Centro, Cordón y Ciudad Vieja", true,
        [
            new(1, 1, "Mañana", new TimeOnly(9, 0), new TimeOnly(13, 0), true),
            new(2, 1, "Tarde", new TimeOnly(14, 0), new TimeOnly(18, 0), true)
        ]) { Departamento = "Montevideo", Localidades = ["Centro", "Cordón", "Ciudad Vieja"] },
        new(2, "Costa", "Pocitos, Buceo y Malvín", true,
        [
            new(3, 2, "Todo el día", new TimeOnly(9, 0), new TimeOnly(18, 0), true)
        ]) { Departamento = "Montevideo", Localidades = ["Pocitos", "Buceo", "Malvín"] }
    ];

    private readonly List<VehiculoConfiguracionDto> vehiculos =
    [
        new(1, "SBA1234", "Furgón", 800m, 6.5m, true),
        new(2, "SBC5678", "Moto", 25m, 0.15m, true)
    ];

    private readonly List<MotivoNoEntregaConfiguracionDto> motivos =
    [
        new(1, "Destinatario ausente", null, true),
        new(2, "Dirección incorrecta", null, true),
        new(3, "Rechazado por el destinatario", null, true)
    ];

    private readonly List<ReglaOperativaConfiguracionDto> reglas =
    [
        new(1, "Estándar", "Entrega en 48 h", "Estandar", 2, 24, 48, 72, true, false, false, true),
        new(2, "Urgente", "Entrega en el día", "Urgente", 1, 4, 12, 24, true, true, false, true)
    ];

    private readonly List<CuadroTarifarioDto> cuadros = [];

    public ConfiguracionApiDePrueba()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        cuadros.Add(new CuadroTarifarioDto(1, 1, hoy.AddMonths(-1), null, EstadosCuadroTarifario.Vigente,
        [
            new(1, "Hasta 5 kg", 1, "Centro", "Estandar", 0m, 5m, 0m, 0.5m, 250m, 30m, true),
            new(2, "Hasta 5 kg", 2, "Costa", "Estandar", 0m, 5m, 0m, 0.5m, 280m, 30m, true),
            new(3, "Urgente hasta 5 kg", 1, "Centro", "Urgente", 0m, 5m, 0m, 0.5m, 420m, 45m, true)
        ]));
    }

    public Task<IReadOnlyList<ZonaDto>> ListarZonasAsync(CancellationToken ct = default)
    {
        lock (candado)
            return Task.FromResult<IReadOnlyList<ZonaDto>>(zonas.OrderByDescending(z => z.Activa).ThenBy(z => z.Nombre).ToList());
    }

    public Task CrearZonaAsync(GuardarZonaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var nombre = Requerido(request.Nombre);
            if (zonas.Any(z => Igual(z.Nombre, nombre)))
                throw new OperacionRechazadaException($"Ya existe una zona llamada \"{nombre}\".");

            zonas.Add(new ZonaDto(++ultimoId, nombre, Opcional(request.Descripcion), true, [])
            {
                Departamento = Opcional(request.Departamento),
                Localidades = request.Localidades ?? []
            });
        }

        return Task.CompletedTask;
    }

    public Task EditarZonaAsync(int id, GuardarZonaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var nombre = Requerido(request.Nombre);
            if (zonas.Any(z => z.Id != id && Igual(z.Nombre, nombre)))
                throw new OperacionRechazadaException($"Ya existe una zona llamada \"{nombre}\".");

            Reemplazar(zonas, z => z.Id == id, z => z with
            {
                Nombre = nombre,
                Descripcion = Opcional(request.Descripcion),
                Departamento = Opcional(request.Departamento),
                Localidades = request.Localidades ?? []
            });
        }

        return Task.CompletedTask;
    }

    public Task CambiarZonaActivaAsync(int id, bool activa, CancellationToken ct = default)
    {
        lock (candado)
            Reemplazar(zonas, z => z.Id == id, z => z with { Activa = activa });

        return Task.CompletedTask;
    }

    public Task CrearFranjaAsync(int zonaId, GuardarFranjaHorariaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var nombre = ValidarFranja(request);
            Reemplazar(zonas, z => z.Id == zonaId, z =>
            {
                if (z.Franjas.Any(f => Igual(f.Nombre, nombre)))
                    throw new OperacionRechazadaException($"La zona ya tiene una franja llamada \"{nombre}\".");

                return z with { Franjas = [.. z.Franjas, new FranjaHorariaDto(++ultimoId, zonaId, nombre, request.HoraInicio, request.HoraFin, true)] };
            });
        }

        return Task.CompletedTask;
    }

    public Task EditarFranjaAsync(int zonaId, int id, GuardarFranjaHorariaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var nombre = ValidarFranja(request);
            Reemplazar(zonas, z => z.Id == zonaId, z =>
            {
                if (z.Franjas.Any(f => f.Id != id && Igual(f.Nombre, nombre)))
                    throw new OperacionRechazadaException($"La zona ya tiene una franja llamada \"{nombre}\".");

                return z with
                {
                    Franjas = z.Franjas.Select(f => f.Id == id ? f with { Nombre = nombre, HoraInicio = request.HoraInicio, HoraFin = request.HoraFin } : f).ToList()
                };
            });
        }

        return Task.CompletedTask;
    }

    public Task CambiarFranjaActivaAsync(int zonaId, int id, bool activa, CancellationToken ct = default)
    {
        lock (candado)
            Reemplazar(zonas, z => z.Id == zonaId, z => z with { Franjas = z.Franjas.Select(f => f.Id == id ? f with { Activa = activa } : f).ToList() });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VehiculoConfiguracionDto>> ListarVehiculosAsync(CancellationToken ct = default)
    {
        lock (candado)
            return Task.FromResult<IReadOnlyList<VehiculoConfiguracionDto>>(vehiculos.OrderByDescending(v => v.Activo).ThenBy(v => v.Matricula).ToList());
    }

    public Task CrearVehiculoAsync(GuardarVehiculoRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var matricula = ValidarVehiculo(request, null);
            vehiculos.Add(new VehiculoConfiguracionDto(++ultimoId, matricula, request.Tipo.Trim(), request.CapacidadPesoKg, request.CapacidadVolumenM3, true));
        }

        return Task.CompletedTask;
    }

    public Task EditarVehiculoAsync(int id, GuardarVehiculoRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var matricula = ValidarVehiculo(request, id);
            Reemplazar(vehiculos, v => v.Id == id, v => v with
            {
                Matricula = matricula,
                Tipo = request.Tipo.Trim(),
                CapacidadPesoKg = request.CapacidadPesoKg,
                CapacidadVolumenM3 = request.CapacidadVolumenM3
            });
        }

        return Task.CompletedTask;
    }

    public Task CambiarVehiculoActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        lock (candado)
            Reemplazar(vehiculos, v => v.Id == id, v => v with { Activo = activo });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MotivoNoEntregaConfiguracionDto>> ListarMotivosAsync(CancellationToken ct = default)
    {
        lock (candado)
            return Task.FromResult<IReadOnlyList<MotivoNoEntregaConfiguracionDto>>(motivos.OrderByDescending(m => m.Activo).ThenBy(m => m.Nombre).ToList());
    }

    public Task CrearMotivoAsync(GuardarMotivoNoEntregaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var nombre = ValidarMotivo(request, null);
            motivos.Add(new MotivoNoEntregaConfiguracionDto(++ultimoId, nombre, Opcional(request.Descripcion), true));
        }

        return Task.CompletedTask;
    }

    public Task EditarMotivoAsync(int id, GuardarMotivoNoEntregaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var nombre = ValidarMotivo(request, id);
            Reemplazar(motivos, m => m.Id == id, m => m with { Nombre = nombre, Descripcion = Opcional(request.Descripcion) });
        }

        return Task.CompletedTask;
    }

    public Task CambiarMotivoActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        lock (candado)
        {
            if (!activo && motivos.Count(m => m.Activo) == 1 && motivos.Any(m => m.Id == id && m.Activo))
                throw new OperacionRechazadaException("Tiene que quedar al menos un motivo activo: el repartidor lo necesita para registrar una entrega fallida.");

            Reemplazar(motivos, m => m.Id == id, m => m with { Activo = activo });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReglaOperativaConfiguracionDto>> ListarReglasAsync(CancellationToken ct = default)
    {
        lock (candado)
            return Task.FromResult<IReadOnlyList<ReglaOperativaConfiguracionDto>>(
                reglas.OrderBy(r => r.Modalidad == "Urgente").ThenByDescending(r => r.Activa).ThenBy(r => r.Nombre).ToList());
    }

    public Task CrearReglaAsync(GuardarReglaOperativaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var regla = ARegla(++ultimoId, request, true);
            DesactivarOtras(regla.Modalidad, regla.Id);
            reglas.Add(regla);
        }

        return Task.CompletedTask;
    }

    public Task EditarReglaAsync(int id, GuardarReglaOperativaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            Reemplazar(reglas, r => r.Id == id, r => ARegla(id, request, r.Activa));
            var regla = reglas.Single(r => r.Id == id);
            if (regla.Activa)
                DesactivarOtras(regla.Modalidad, id);
        }

        return Task.CompletedTask;
    }

    public Task CambiarReglaActivaAsync(int id, bool activa, CancellationToken ct = default)
    {
        lock (candado)
        {
            Reemplazar(reglas, r => r.Id == id, r => r with { Activa = activa });
            if (activa)
                DesactivarOtras(reglas.Single(r => r.Id == id).Modalidad, id);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CuadroTarifarioDto>> ListarCuadrosAsync(CancellationToken ct = default)
    {
        lock (candado)
            return Task.FromResult<IReadOnlyList<CuadroTarifarioDto>>(cuadros.OrderByDescending(c => c.Version).ToList());
    }

    public Task<CuadroTarifarioDto?> ObtenerCuadroAsync(int id, CancellationToken ct = default)
    {
        lock (candado)
            return Task.FromResult(cuadros.FirstOrDefault(c => c.Id == id));
    }

    public Task<CuadroTarifarioDto> CrearCuadroAsync(CrearCuadroTarifarioRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            if (request.VigenciaDesde <= DateOnly.FromDateTime(DateTime.Today))
                throw new OperacionRechazadaException("El cuadro nuevo tiene que empezar a regir a partir de mañana.");

            var anterior = cuadros.MaxBy(c => c.Version);
            if (anterior is not null && request.VigenciaDesde <= anterior.VigenciaDesde)
                throw new OperacionRechazadaException(
                    $"Tiene que empezar después del {anterior.VigenciaDesde:dd/MM/yyyy}, que es cuando empieza la versión {anterior.Version}.");

            IReadOnlyList<TarifaDto> tarifas = request.CopiarTarifasDeLaAnterior && anterior is not null
                ? anterior.Tarifas.Where(t => t.Activa).Select(t => t with { Id = ++ultimoId }).ToList()
                : [];

            if (anterior is not null)
                Reemplazar(cuadros, c => c.Id == anterior.Id, c => c with { VigenciaHasta = request.VigenciaDesde });

            var cuadro = new CuadroTarifarioDto(++ultimoId, (anterior?.Version ?? 0) + 1, request.VigenciaDesde, null,
                EstadosCuadroTarifario.Programado, tarifas);
            cuadros.Add(cuadro);
            return Task.FromResult(cuadro);
        }
    }

    public Task AgregarTarifaAsync(int cuadroId, GuardarTarifaRequest request, CancellationToken ct = default)
    {
        lock (candado)
        {
            var tarifa = ATarifa(++ultimoId, request, true);
            CambiarCuadro(cuadroId, c => c with { Tarifas = [.. c.Tarifas, tarifa] });
        }

        return Task.CompletedTask;
    }

    public Task EditarTarifaAsync(int cuadroId, int id, GuardarTarifaRequest request, CancellationToken ct = default)
    {
        lock (candado)
            CambiarCuadro(cuadroId, c => c with { Tarifas = c.Tarifas.Select(t => t.Id == id ? ATarifa(id, request, t.Activa) : t).ToList() });

        return Task.CompletedTask;
    }

    public Task CambiarTarifaActivaAsync(int cuadroId, int id, bool activa, CancellationToken ct = default)
    {
        lock (candado)
            CambiarCuadro(cuadroId, c => c with { Tarifas = c.Tarifas.Select(t => t.Id == id ? t with { Activa = activa } : t).ToList() });

        return Task.CompletedTask;
    }

    private void CambiarCuadro(int cuadroId, Func<CuadroTarifarioDto, CuadroTarifarioDto> cambio) =>
        Reemplazar(cuadros, c => c.Id == cuadroId, c =>
        {
            if (!c.Editable)
                throw new OperacionRechazadaException("Ese cuadro ya entró en vigencia y no se puede cambiar: creá una versión nueva.");

            return cambio(c);
        });

    private TarifaDto ATarifa(int id, GuardarTarifaRequest request, bool activa)
    {
        var zona = zonas.FirstOrDefault(z => z.Id == request.ZonaId)
            ?? throw new OperacionRechazadaException("La zona no existe.");

        if (request.PesoMinimoKg >= request.PesoMaximoKg)
            throw new OperacionRechazadaException("El peso mínimo tiene que ser menor que el máximo.");

        if (request.VolumenMinimoM3 >= request.VolumenMaximoM3)
            throw new OperacionRechazadaException("El volumen mínimo tiene que ser menor que el máximo.");

        return new TarifaDto(id, Requerido(request.Nombre), zona.Id, zona.Nombre, request.Modalidad,
            request.PesoMinimoKg, request.PesoMaximoKg, request.VolumenMinimoM3, request.VolumenMaximoM3,
            request.PrecioBase, request.PrecioPorKgAdicional, activa);
    }

    private static ReglaOperativaConfiguracionDto ARegla(int id, GuardarReglaOperativaRequest r, bool activa) =>
        new(id, Requerido(r.Nombre), Opcional(r.Descripcion), r.Modalidad, r.MaximoIntentosEntrega, r.HorasEntreIntentos,
            r.PlazoEntregaHoras, r.HorasParaIniciarDevolucion, r.RequiereFirma, r.RequiereFotografia, r.RequiereDocumentoReceptor, activa);

    private void DesactivarOtras(string modalidad, int excepto)
    {
        for (var i = 0; i < reglas.Count; i++)
        {
            if (reglas[i].Id != excepto && reglas[i].Modalidad == modalidad)
                reglas[i] = reglas[i] with { Activa = false };
        }
    }

    private string ValidarVehiculo(GuardarVehiculoRequest request, int? id)
    {
        var matricula = Requerido(request.Matricula).ToUpperInvariant();
        Requerido(request.Tipo);

        if (request.CapacidadPesoKg <= 0 || request.CapacidadVolumenM3 <= 0)
            throw new OperacionRechazadaException("Las capacidades tienen que ser mayores que cero.");

        if (vehiculos.Any(v => v.Id != id && v.Matricula == matricula))
            throw new OperacionRechazadaException($"Ya hay un vehículo con la matrícula {matricula}.");

        return matricula;
    }

    private string ValidarMotivo(GuardarMotivoNoEntregaRequest request, int? id)
    {
        var nombre = Requerido(request.Nombre);

        if (motivos.Any(m => m.Id != id && Igual(m.Nombre, nombre)))
            throw new OperacionRechazadaException($"Ya existe un motivo llamado \"{nombre}\".");

        return nombre;
    }

    private static string ValidarFranja(GuardarFranjaHorariaRequest request)
    {
        if (request.HoraFin <= request.HoraInicio)
            throw new OperacionRechazadaException("La hora de fin tiene que ser posterior a la de inicio.");

        return Requerido(request.Nombre);
    }

    private static string Requerido(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? throw new OperacionRechazadaException("Completá los campos obligatorios.") : valor.Trim();

    private static string? Opcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static bool Igual(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    private static void Reemplazar<T>(List<T> lista, Func<T, bool> cual, Func<T, T> cambio)
    {
        var indice = lista.FindIndex(x => cual(x));
        if (indice < 0)
            throw new OperacionRechazadaException("Lo que querías cambiar ya no existe. Recargá la página.");

        lista[indice] = cambio(lista[indice]);
    }
}
