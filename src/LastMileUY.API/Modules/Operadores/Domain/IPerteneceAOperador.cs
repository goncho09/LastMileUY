namespace LastMileUY.API.Modules.Operadores.Domain;

// Marca las entidades que son de un operador (multitenancy, ADR-01).
// Las que la implementan se filtran solas por el operador actual
// y tienen política RLS en la base.
public interface IPerteneceAOperador
{
    int OperadorId { get; set; }
}
