namespace LastMileUY.Backoffice.Servicios;

// Con los datos de prueba no hay API contra la cual validar: cualquier email y contraseña
// entran como un operador de prueba. Solo se usa con Api:UsarDatosDePrueba = true.
// Para probar los permisos: un email que empiece con "despachador" entra como Despachador.
public class AutenticacionApiDePrueba : IAutenticacionApi
{
    public Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<SesionIniciada?>(null);
        }

        email = email.Trim();
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
