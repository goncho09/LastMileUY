namespace LastMileUY.Contracts.Operadores;

// PUT /api/operador/deposito: de dónde salen las rutas del operador. Las dos en null lo borra.
public class GuardarDepositoRequest
{
    public decimal? Latitud { get; set; }

    public decimal? Longitud { get; set; }
}
