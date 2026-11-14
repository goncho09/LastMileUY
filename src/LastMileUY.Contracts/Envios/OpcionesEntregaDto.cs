namespace LastMileUY.Contracts.Envios;

public record OpcionesEntregaDto(string? Zona, IReadOnlyList<FranjaEntregaDto> Franjas);

public record FranjaEntregaDto(int Id, string Nombre, TimeOnly HoraInicio, TimeOnly HoraFin);
