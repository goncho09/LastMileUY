using System.Text.Json;
using Microsoft.JSInterop;

namespace LastMileUY.DriverPWA.Services;

// Lo que la app guarda en el celular para trabajar sin conexión (la hoja de ruta, las operaciones
// pendientes con su firma y su foto). Son datos personales de los destinatarios, así que todo lo que
// se guarde tiene que pasar por acá, que se encarga de:
//   - cifrarlo (wwwroot/js/almacen.js: AES-GCM con una clave del navegador que no se puede exportar);
//   - que venza: un dato vencido no se devuelve y se borra;
//   - que sea de un solo usuario: al cerrar sesión se borra todo, y si inicia sesión otro
//     repartidor en el mismo celular, también.
// Reglas completas: docs/datos-locales.md
public class AlmacenLocal(IJSRuntime js, TimeProvider reloj)
{
    // Ningún dato tiene que quedar más que una jornada: la hoja de mañana se descarga mañana
    public static readonly TimeSpan DuracionMaxima = TimeSpan.FromHours(24);

    private const string ClaveDueno = "dueno";

    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    private record Entrada<T>(T Valor, DateTimeOffset VenceEn);

    // duracion: hasta DuracionMaxima (si se pide más, se recorta)
    public async Task GuardarAsync<T>(string nombre, T valor, TimeSpan duracion)
    {
        if (duracion > DuracionMaxima)
            duracion = DuracionMaxima;

        var entrada = new Entrada<T>(valor, reloj.GetUtcNow() + duracion);

        await js.InvokeVoidAsync("lastMileAlmacen.guardar", nombre, JsonSerializer.Serialize(entrada, OpcionesJson));
    }

    // null si no hay nada, no se puede leer o ya venció (en ese caso, además, se borra)
    public async Task<T?> LeerAsync<T>(string nombre)
    {
        var texto = await js.InvokeAsync<string?>("lastMileAlmacen.leer", nombre);
        var entrada = texto is null ? null : JsonSerializer.Deserialize<Entrada<T>>(texto, OpcionesJson);

        if (entrada is null)
            return default;

        if (entrada.VenceEn <= reloj.GetUtcNow())
        {
            await BorrarAsync(nombre);
            return default;
        }

        return entrada.Valor;
    }

    public async Task BorrarAsync(string nombre) =>
        await js.InvokeVoidAsync("lastMileAlmacen.borrar", nombre);

    // Todo, con la clave de cifrado incluida
    public async Task BorrarTodoAsync() =>
        await js.InvokeVoidAsync("lastMileAlmacen.borrarTodo");

    // Al iniciar sesión: si lo guardado no es de este usuario, se borra antes de que lo vea.
    // Si no se sabe de quién es (el dato del dueño venció o no se puede leer), también.
    public async Task AsignarDuenoAsync(string usuarioId)
    {
        var dueno = await LeerAsync<string>(ClaveDueno);

        if (dueno != usuarioId)
            await BorrarTodoAsync();

        await GuardarAsync(ClaveDueno, usuarioId, DuracionMaxima);
    }
}
