namespace LastMileUY.Backoffice.Servicios;

// Con los datos de prueba no hay API contra la cual validar: cualquier email y contraseña
// entran como un operador de prueba. Solo se usa con Api:UsarDatosDePrueba = true.
public class AutenticacionApiDePrueba : IAutenticacionApi
{
    public Task<SesionIniciada?> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<SesionIniciada?>(null);
        }

        return Task.FromResult<SesionIniciada?>(
            new SesionIniciada("Operador de prueba", email.Trim(), 1, null));
    }
}
