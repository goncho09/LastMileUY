
using System.Text.Json;

public sealed class RegistroClavesAvisos
{
    private readonly string _rutaArchivo;
    private readonly object _bloqueo = new();
    private readonly Dictionary<int, string> _claves;

    public RegistroClavesAvisos(IConfiguration configuracion)
    {
        _rutaArchivo = configuracion["Avisos:RutaClaves"]
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "claves-avisos.json");

        var directorio = Path.GetDirectoryName(_rutaArchivo)!;
        Directory.CreateDirectory(directorio);

        if (File.Exists(_rutaArchivo))
        {
            var contenido = File.ReadAllText(_rutaArchivo);

            _claves = JsonSerializer.Deserialize<Dictionary<int, string>>(
                contenido) ?? new Dictionary<int, string>();
        }
        else
        {
            _claves = new Dictionary<int, string>();
        }
    }

    public string? Obtener(int suscripcionId)
    {
        lock (_bloqueo)
        {
            return _claves.GetValueOrDefault(suscripcionId);
        }
    }

    public void Registrar(int suscripcionId, string claveFirma)
    {
        if (suscripcionId <= 0)
            throw new ArgumentException(
                "El identificador de suscripción debe ser positivo.");

        byte[] clave;

        try
        {
            clave = Convert.FromHexString(claveFirma);
        }
        catch (FormatException)
        {
            throw new ArgumentException(
                "La clave debe estar en formato hexadecimal.");
        }

        if (clave.Length != 32)
            throw new ArgumentException(
                "La clave debe contener exactamente 32 bytes.");

        lock (_bloqueo)
        {
            var nuevasClaves = new Dictionary<int, string>(_claves)
            {
                [suscripcionId] = Convert.ToHexString(clave)
            };

            var json = JsonSerializer.Serialize(
                nuevasClaves,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            var archivoTemporal = _rutaArchivo + "." +
                Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                File.WriteAllText(archivoTemporal, json);
                File.Move(
                    archivoTemporal,
                    _rutaArchivo,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(archivoTemporal))
                    File.Delete(archivoTemporal);
            }

            _claves.Clear();

            foreach (var (id, claveGuardada) in nuevasClaves)
                _claves[id] = claveGuardada;
        }
    }

    public int[] ListarSuscripciones()
    {
        lock (_bloqueo)
        {
            return _claves.Keys.OrderBy(id => id).ToArray();
        }
    }
}
