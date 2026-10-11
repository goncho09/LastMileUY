namespace LastMileUY.Contracts.Auth;

public class UsuarioAutenticadoResponse
{
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    public string Apellido { get; set; } = string.Empty;

    public int? OperadorId { get; set; }

    public int? ComercioId { get; set; }

    public IList<string> Roles { get; set; } = [];
}