namespace LastMileUY.DriverPWA.Services;

public static class TextoEstadoRuta
{
    public static string De(string estado) => estado switch
    {
        "Planificada" => "Planificada",
        "Despachada" => "Lista para salir",
        "EnCurso" => "En curso",
        "Finalizada" => "Finalizada",
        _ => estado
    };
}
