namespace LastMileUY.API.Modules.Destinatarios.Domain;

using LastMileUY.API.Modules.Envios.Domain;

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
    
    public ICollection<Envio> Envios { get; set; } = new List<Envio>();
}