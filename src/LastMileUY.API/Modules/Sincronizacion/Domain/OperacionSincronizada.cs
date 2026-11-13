using LastMileUY.API.Modules.Entregas.Domain;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Sincronizacion.Domain;

public class OperacionSincronizada : IPerteneceAOperador
{
    public int Id { get; set; }

    public int OperadorId { get; set; }

    public Guid IdOperacion { get; set; }

    public string? Huella { get; set; }

    public string UsuarioId { get; set; } = string.Empty;

    public TipoOperacion Tipo { get; set; }

    public int RutaId { get; set; }

    public int EnvioId { get; set; }

    public ResultadoOperacion Resultado { get; set; }

    public string Mensaje { get; set; } = string.Empty;

    public DateTime FechaHoraDispositivo { get; set; }

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    public int? IntentoEntregaId { get; set; }

    public IntentoEntrega? IntentoEntrega { get; set; }

    public int? ReglaEntregaParcialId { get; set; }

    public ReglaEntregaParcial? ReglaEntregaParcial { get; set; }
}

public enum TipoOperacion
{
    Entrega = 1,
    IntentoFallido = 2,
    EntregaParcial = 3
}

public enum ResultadoOperacion
{
    Aplicada = 1,

    Rechazada = 2,

    Conflicto = 3
}
