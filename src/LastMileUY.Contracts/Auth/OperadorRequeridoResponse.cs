namespace LastMileUY.Contracts.Auth;

// Respuesta del login cuando el comercio debe seleccionar un operador.
// Incluye una autorización temporal para completar el inicio de sesión
// sin volver a ingresar la contraseña.
public class OperadorRequeridoResponse
{
    public string Mensaje { get; set; } = string.Empty;

    public List<OperadorDisponibleDto> Operadores { get; set; } = [];

    public string TokenTemporal { get; set; } = string.Empty;
}