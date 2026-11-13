namespace LastMileUY.API.Modules.Notificaciones.Domain;

public class IntentoAviso
{
    public int Id { get; set; }

    public int EntregaAvisoId { get; set; }

    public int NumeroIntento { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    public bool Exitoso { get; set; }

    public int? CodigoHttp { get; set; }

    public string? Error { get; set; }

    public int DuracionMs { get; set; }
}
