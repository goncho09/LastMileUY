
using System.Security.Cryptography;
using System.Text;

public sealed class ClaveAdministrativaReceptor
{
    private readonly byte[] _clave;

    public ClaveAdministrativaReceptor(IConfiguration configuracion)
    {
        var rutaClaves = configuracion["Avisos:RutaClaves"]
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "claves-avisos.json");

        var directorio = Path.GetDirectoryName(rutaClaves)!;
        Directory.CreateDirectory(directorio);

        var rutaAdministrativa = Path.Combine(
            directorio,
            "admin-key");

        if (File.Exists(rutaAdministrativa))
        {
            var claveGuardada = File.ReadAllText(rutaAdministrativa).Trim();

            if (!EsClaveValida(claveGuardada))
            {
                throw new InvalidOperationException(
                    "La clave administrativa almacenada no es válida.");
            }

            _clave = Encoding.UTF8.GetBytes(claveGuardada);
            return;
        }

        // Se genera solamente si todavía no existe.
        var claveNueva = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));

        // CreateNew evita sobrescribir una clave existente.
        try
        {
            using var archivo = new FileStream(
                rutaAdministrativa,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            using var escritor = new StreamWriter(archivo);
            escritor.Write(claveNueva);
        }
        catch (IOException) when (File.Exists(rutaAdministrativa))
        {
            // Otro proceso pudo crearla al mismo tiempo.
            claveNueva = File.ReadAllText(rutaAdministrativa).Trim();
        }

        if (!EsClaveValida(claveNueva))
        {
            throw new InvalidOperationException(
                "No se pudo inicializar la clave administrativa.");
        }

        _clave = Encoding.UTF8.GetBytes(claveNueva);
    }

    public bool Verificar(string? claveRecibida)
    {
        if (string.IsNullOrEmpty(claveRecibida))
            return false;

        var bytesRecibidos = Encoding.UTF8.GetBytes(claveRecibida);

        return CryptographicOperations.FixedTimeEquals(
            _clave,
            bytesRecibidos);
    }

    private static bool EsClaveValida(string clave)
    {
        if (clave.Length != 64)
            return false;

        try
        {
            return Convert.FromHexString(clave).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
