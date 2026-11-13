using System.Text.Json;
using Microsoft.JSInterop;

namespace LastMileUY.DriverPWA.Services;

public class AlmacenLocal(IJSRuntime js, TimeProvider reloj)
{
    public static readonly TimeSpan DuracionMaxima = TimeSpan.FromHours(24);

    private const string ClaveDueno = "dueno";

    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    private record Entrada<T>(T Valor, DateTimeOffset VenceEn);

    public async Task GuardarAsync<T>(string nombre, T valor, TimeSpan duracion)
    {
        if (duracion > DuracionMaxima)
            duracion = DuracionMaxima;

        var entrada = new Entrada<T>(valor, reloj.GetUtcNow() + duracion);

        await js.InvokeVoidAsync("lastMileAlmacen.guardar", nombre, JsonSerializer.Serialize(entrada, OpcionesJson));
    }

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

    public async Task GuardarPendienteAsync<T>(string nombre, T valor)
    {
        await js.InvokeVoidAsync(
            "lastMileAlmacen.guardar",
            nombre,
            JsonSerializer.Serialize(valor, OpcionesJson));
    }

    public async Task<T?> LeerPendienteAsync<T>(string nombre)
    {
        var texto = await js.InvokeAsync<string?>(
            "lastMileAlmacen.leer",
            nombre);

        return texto is null
            ? default
            : JsonSerializer.Deserialize<T>(texto, OpcionesJson);
    }

    public async Task BorrarAsync(string nombre) =>
        await js.InvokeVoidAsync("lastMileAlmacen.borrar", nombre);

    public async Task BorrarTodoAsync() =>
        await js.InvokeVoidAsync("lastMileAlmacen.borrarTodo");

    public async Task AsignarDuenoAsync(string usuarioId)
    {
        string? dueno;

        try
        {
            dueno = await LeerPendienteAsync<string>(ClaveDueno);
        }
        catch (JsonException)
        {
            dueno = null;
        }

        if (dueno != usuarioId)
            await BorrarTodoAsync();

        await GuardarPendienteAsync(ClaveDueno, usuarioId);
    }
}
