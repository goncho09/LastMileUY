using LastMileUY.Contracts.Rutas;

namespace LastMileUY.Contracts.Operadores;

// El operador del usuario logueado: su identidad visual y su configuración (GET /api/operador)
public record OperadorActualDto(
    int Id,
    string Nombre,
    string? ColorPrimario,
    string? LogoUrl,
    IReadOnlyList<ReglaOperativaDto> Reglas,
    // De dónde salen sus rutas (null si no lo cargó): el punto de partida del orden sugerido
    Coordenadas? Deposito = null,
    // Las claves de las funcionalidades prendidas para el operador (ver Funcionalidades)
    IReadOnlyList<string>? Funcionalidades = null)
{
    // Sin la lista (una API vieja o datos de prueba que no la cargan) vale el valor por defecto
    public bool Tiene(string funcionalidad) =>
        Funcionalidades?.Contains(funcionalidad)
        ?? global::LastMileUY.Contracts.Operadores.Funcionalidades.Buscar(funcionalidad)?.PorDefecto
        ?? false;
}

// Una regla operativa activa del operador (hay como máximo una por modalidad)
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
