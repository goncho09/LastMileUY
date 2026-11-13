namespace LastMileUY.Contracts.Auth;

public class OperadorRequeridoResponse
{
    public string Mensaje { get; set; } = string.Empty;

    public List<OperadorDisponibleDto> Operadores { get; set; } = [];

    public string TokenTemporal { get; set; } = string.Empty;
}