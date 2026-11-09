namespace LastMileUY.Contracts.Operadores;

// Banderas de funcionalidad por operador: partes del sistema que el administrador de la plataforma
// prende o apaga para cada operador, sin desplegar de nuevo (docs/funcionalidades.md).
// Sin una decisión guardada, cada una vale lo que dice PorDefecto.
public static class Funcionalidades
{
    public const string OrdenSugerido = "orden-sugerido";
    public const string SeguimientoPublico = "seguimiento-publico";
    public const string AvisosComercios = "avisos-comercios";

    public static readonly IReadOnlyList<DefinicionFuncionalidad> Todas =
    [
        new(OrdenSugerido,
            "Orden sugerido de paradas",
            "Al armar una hoja de ruta, \"Ordenar por recorrido\" propone el orden que recorre menos kilómetros.",
            PorDefecto: true),
        new(SeguimientoPublico,
            "Seguimiento público",
            "El destinatario consulta el estado de su envío con el código de seguimiento, sin iniciar sesión.",
            PorDefecto: true),
        new(AvisosComercios,
            "Avisos a comercios",
            "Los comercios se suscriben para recibir por HTTP cada cambio de estado de sus envíos.",
            PorDefecto: true)
    ];

    public static DefinicionFuncionalidad? Buscar(string clave) =>
        Todas.FirstOrDefault(f => f.Clave == clave);
}

public record DefinicionFuncionalidad(string Clave, string Nombre, string Descripcion, bool PorDefecto);

// Cómo está una funcionalidad para un operador. Activa: lo que vale ahora (lo guardado o, si no se
// decidió nada, el valor por defecto)
public record FuncionalidadDto(string Clave, string Nombre, string Descripcion, bool Activa, bool PorDefecto);

// Todas las funcionalidades de un operador (GET /api/admin/funcionalidades)
public record FuncionalidadesOperadorDto(int OperadorId, string Operador, IReadOnlyList<FuncionalidadDto> Funcionalidades);

// PUT /api/admin/operadores/{operadorId}/funcionalidades/{clave}
public class CambiarFuncionalidadRequest
{
    public bool Activa { get; set; }
}
