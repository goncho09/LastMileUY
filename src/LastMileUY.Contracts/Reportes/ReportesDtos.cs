namespace LastMileUY.Contracts.Reportes;

public record CumplimientoDto(
    string Nombre,
    int Envios,
    int Entregados,
    int ATiempo,
    int NoEntregados)
{
    public decimal PorcentajeEntregados => Envios == 0 ? 0 : Math.Round(Entregados * 100m / Envios, 1);

    public decimal PorcentajeATiempo => Envios == 0 ? 0 : Math.Round(ATiempo * 100m / Envios, 1);
}

public record EfectividadRepartidorDto(string Repartidor, int Intentos, int Exitosos)
{
    public decimal Porcentaje => Intentos == 0 ? 0 : Math.Round(Exitosos * 100m / Intentos, 1);
}

public record MotivoNoEntregaReporteDto(string Motivo, int Cantidad);

public record VolumenDiaDto(DateOnly Dia, int Envios, int Entregados);

public record LiquidacionComercioDto(
    int ComercioId,
    string Comercio,
    int Envios,
    int Entregados,
    int Devueltos,
    int SinTarifa,
    decimal Importe);

public record ReporteGestionDto(
    DateOnly Desde,
    DateOnly Hasta,
    int TotalEnvios,
    IReadOnlyList<VolumenDiaDto> Volumen,
    IReadOnlyList<CumplimientoDto> PorZona,
    IReadOnlyList<CumplimientoDto> PorComercio,
    IReadOnlyList<EfectividadRepartidorDto> PorRepartidor,
    IReadOnlyList<MotivoNoEntregaReporteDto> Motivos,
    IReadOnlyList<LiquidacionComercioDto> Liquidacion)
{
    public IReadOnlyList<IndicadorDiarioDto> Cierres { get; init; } = [];
}

public record IndicadorDiarioDto(
    DateOnly Dia,
    int EnviosCreados,
    int Entregados,
    int EntregadosATiempo,
    int NoEntregados,
    int IntentosFallidos,
    decimal Importe,
    bool Cerrado,
    DateTime CalculadoEn);
