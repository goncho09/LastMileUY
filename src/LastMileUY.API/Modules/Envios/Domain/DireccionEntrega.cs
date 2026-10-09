namespace LastMileUY.API.Modules.Envios.Domain;

public record DireccionEntrega(
    string Calle,
    string Numero,
    string? Apartamento,
    string Ciudad,
    string Departamento,
    string? CodigoPostal,
    string? Referencia
);