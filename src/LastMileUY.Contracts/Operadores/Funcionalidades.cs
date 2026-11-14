namespace LastMileUY.Contracts.Operadores;

public static class Funcionalidades
{
    public const string OrdenSugerido = "orden-sugerido";
    public const string SeguimientoPublico = "seguimiento-publico";
    public const string AvisosComercios = "avisos-comercios";
    public const string VerificacionCarga = "verificacion-carga";

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
            PorDefecto: true),
        new(VerificacionCarga,
            "Verificación de carga obligatoria",
            "Una hoja de ruta solo se despacha si el depósito verificó que todos sus bultos se cargaron en el vehículo.",
            PorDefecto: false)
    ];

    public static DefinicionFuncionalidad? Buscar(string clave) =>
        Todas.FirstOrDefault(f => f.Clave == clave);
}

public record DefinicionFuncionalidad(string Clave, string Nombre, string Descripcion, bool PorDefecto);

public record FuncionalidadDto(string Clave, string Nombre, string Descripcion, bool Activa, bool PorDefecto);

public record FuncionalidadesOperadorDto(int OperadorId, string Operador, IReadOnlyList<FuncionalidadDto> Funcionalidades);

public class CambiarFuncionalidadRequest
{
    public bool Activa { get; set; }
}
