namespace LastMileUY.Contracts.Destinatarios;

// Los 19 departamentos, escritos como se guardan. La API acepta también sin tildes y en cualquier
// mayúscula; el portal los ofrece en una lista
public static class DepartamentosUruguay
{
    public static readonly IReadOnlyList<string> Todos =
    [
        "Artigas", "Canelones", "Cerro Largo", "Colonia", "Durazno", "Flores", "Florida", "Lavalleja",
        "Maldonado", "Montevideo", "Paysandú", "Río Negro", "Rivera", "Rocha", "Salto", "San José",
        "Soriano", "Tacuarembó", "Treinta y Tres"
    ];
}
