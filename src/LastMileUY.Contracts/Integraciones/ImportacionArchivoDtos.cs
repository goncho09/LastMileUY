namespace LastMileUY.Contracts.Integraciones;

public record ImportarArchivoRequest(string Contenido, string? NombreArchivo = null);

public static class ColumnasImportacion
{
    public const string Referencia = "referencia";
    public const string Nombre = "nombre";
    public const string Apellido = "apellido";
    public const string Email = "email";
    public const string Telefono = "telefono";
    public const string Calle = "calle";
    public const string Numero = "numero";
    public const string Apartamento = "apartamento";
    public const string Ciudad = "ciudad";
    public const string Departamento = "departamento";
    public const string Modalidad = "modalidad";
    public const string Bultos = "bultos";
    public const string PesoKg = "peso_kg";
    public const string LargoCm = "largo_cm";
    public const string AnchoCm = "ancho_cm";
    public const string AltoCm = "alto_cm";
    public const string Descripcion = "descripcion";

    public static readonly string[] Obligatorias =
        [Referencia, Nombre, Apellido, Calle, Numero, Ciudad, Departamento, PesoKg, LargoCm, AnchoCm, AltoCm];

    public static readonly string[] Todas =
        [Referencia, Nombre, Apellido, Email, Telefono, Calle, Numero, Apartamento, Ciudad, Departamento,
         Modalidad, Bultos, PesoKg, LargoCm, AnchoCm, AltoCm, Descripcion];

    public static string Plantilla =>
        string.Join(';', Todas) + "\n"
        + "PED-1001;Ana;Silva;ana@correo.com;099123456;Av. Brasil;2550;301;Montevideo;Montevideo;Estandar;1;2,5;30;20;15;Ropa\n";
}
