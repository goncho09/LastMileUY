namespace LastMileUY.Contracts.Auth;

public sealed class SeleccionarOperadorRequest
{
    public string TokenTemporal { get; set; } = string.Empty;
    public int OperadorId { get; set; }
}