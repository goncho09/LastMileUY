using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Auth;

public class CambiarOperadorRequest
{
    [Range(1, int.MaxValue)]
    public int OperadorId { get; set; }
}
