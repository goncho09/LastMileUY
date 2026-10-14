namespace LastMileUY.Contracts.Auth;

// Respuesta 409 del login cuando el comercio trabaja con varios operadores y no eligió ninguno:
// el cliente muestra la lista y vuelve a llamar al login con el OperadorId elegido.
public class OperadorRequeridoResponse
{
    public string Mensaje { get; set; } = string.Empty;

    public List<OperadorDisponibleDto> Operadores { get; set; } = [];
}
