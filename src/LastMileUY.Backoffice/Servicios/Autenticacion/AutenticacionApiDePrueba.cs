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

        var esDespachador = email.StartsWith(Permisos.Despachador, StringComparison.OrdinalIgnoreCase);

        return Task.FromResult<SesionIniciada?>(new SesionIniciada(
            esDespachador ? "Despachador de prueba" : "Operador de prueba",
            email,
            1,
            null,
            OperadorApiDePrueba.Operador,
            [esDespachador ? Permisos.Despachador : Permisos.Operador]));
    }
}
