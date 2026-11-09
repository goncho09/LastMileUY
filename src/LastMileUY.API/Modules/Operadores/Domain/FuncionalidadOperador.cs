namespace LastMileUY.API.Modules.Operadores.Domain;

// Lo que decidió el administrador sobre una funcionalidad para un operador (ver Contracts.Operadores.Funcionalidades).
// Sin fila, vale el valor por defecto. Es de la plataforma, no del operador: lo cambia el administrador,
// que no tiene operador; por eso no implementa IPerteneceAOperador ni tiene RLS, como Operadores.
public class FuncionalidadOperador
{
    public int OperadorId { get; set; }

    public string Clave { get; set; } = string.Empty;

    public bool Activa { get; set; }

    public DateTime FechaModificacion { get; set; } = DateTime.UtcNow;

    // Id del usuario administrador que la cambió por última vez
    public string? ModificadaPor { get; set; }
}
