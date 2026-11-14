namespace LastMileUY.Backoffice.Servicios;

public class AutenticacionApiDePrueba : IAutenticacionApi
{
    public Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<SesionIniciada?>(null);
        }

        email = email.Trim();

        if (email.StartsWith(Permisos.Administrador, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<SesionIniciada?>(new SesionIniciada(
                "Administrador de prueba", email, null, null, null, [Permisos.Administrador]));
        }

        var (nombre, rol) =
            email.StartsWith(Permisos.Despachador, StringComparison.OrdinalIgnoreCase) ? ("Despachador de prueba", Permisos.Despachador)
            : email.StartsWith("deposito", StringComparison.OrdinalIgnoreCase) ? ("Depósito de prueba", Permisos.OperarioDeposito)
            : ("Operador de prueba", Permisos.Operador);

        return Task.FromResult<SesionIniciada?>(new SesionIniciada(
            nombre,
            email,
            1,
            null,
            OperadorApiDePrueba.Operador,
            [rol]));
    }
}
