namespace LastMileUY.API.Modules.Destinatarios.Domain;

public class Direccion
{
    public int Id { get; set; }

    public int DestinatarioId { get; set; }

    public string Calle { get; set; } = string.Empty;

    public string Numero { get; set; } = string.Empty;

    public string? Apartamento { get; set; }

    public string Ciudad { get; set; } = string.Empty;

    public string Departamento { get; set; } = string.Empty;

    public string? CodigoPostal { get; set; }

    public string? Referencia { get; set; }

    public bool Activa { get; set; } = true;

    // Dónde queda en el mapa (grados decimales). Opcional: sin ella, el envío no entra en el orden
    // sugerido de la hoja de ruta (docs/hojas-de-ruta.md)
    public decimal? Latitud { get; set; }

    public decimal? Longitud { get; set; }
    
}