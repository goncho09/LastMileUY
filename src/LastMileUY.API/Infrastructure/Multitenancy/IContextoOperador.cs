namespace LastMileUY.API.Infrastructure.Multitenancy;

public interface IContextoOperador
{
    int? OperadorId { get; }

    int? ComercioId { get; }

    bool EnAmbientePruebas => false;
}
