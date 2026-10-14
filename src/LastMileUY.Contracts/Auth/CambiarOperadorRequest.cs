using System.ComponentModel.DataAnnotations;

namespace LastMileUY.Contracts.Auth;

// POST /api/auth/operador: el usuario de comercio pasa a trabajar con otro de sus operadores
public class CambiarOperadorRequest
{
    [Range(1, int.MaxValue)]
    public int OperadorId { get; set; }
}
