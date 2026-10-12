namespace LastMileUY.API.Modules.Comercios.Domain;

// Marca las entidades que, además de ser de un operador, son de un comercio
// (segundo nivel del multitenancy, ADR-01). Un usuario de comercio solo ve las de su comercio.
public interface IPerteneceAComercio
{
    int ComercioId { get; set; }
}
