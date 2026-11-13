using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Contracts.Operadores;

public record OperadorActualDto(
    int Id,
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl,
    IReadOnlyList<ReglaOperativaDto> Reglas,
    Coordenadas? Deposito = null,
    IReadOnlyList<string>? Funcionalidades = null)
{
    public bool Tiene(string funcionalidad) =>
        Funcionalidades?.Contains(funcionalidad)
        ?? global::LastMileUY.Contracts.Operadores.Funcionalidades.Buscar(funcionalidad)?.PorDefecto
        ?? false;
}

public record ReglaOperativaDto(
    string Modalidad,
    string Nombre,
    string? Descripcion,
    int MaximoIntentosEntrega,
    int HorasEntreIntentos,
    int PlazoEntregaHoras,
    int HorasParaIniciarDevolucion,
    bool RequiereFirma,
    bool RequiereFotografia,
    bool RequiereDocumentoReceptor);
