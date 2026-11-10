namespace LastMileUY.Backoffice.Servicios;

// Por dónde entró un envío, como lo devuelve la API, y cómo se muestra
public static class CanalesIngreso
{
    public const string Portal = "Portal";
    public const string Api = "Api";

    public static readonly (string Valor, string Texto)[] Todos =
    [
        (Portal, "Portal del comercio"),
        (Api, "API")
    ];

    public static string Texto(string canal) => canal == Portal ? "Portal del comercio" : "API";

    public static string ClaseCss(string canal) => canal == Portal ? "text-bg-info" : "text-bg-light border";
}
