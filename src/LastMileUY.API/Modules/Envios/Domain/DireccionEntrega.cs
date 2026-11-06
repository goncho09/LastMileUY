namespace LastMileUY.API.Modules.Envios.Domain;

public record DireccionEntrega(
    string Calle,
    string Numero,
    string? Apartamento,
    string Ciudad,
    string Departamento,
    string? CodigoPostal,
    string? Referencia,
    // Dónde queda, copiado de la dirección del destinatario (null si no tiene): para el orden sugerido
    decimal? Latitud = null,
    decimal? Longitud = null
);