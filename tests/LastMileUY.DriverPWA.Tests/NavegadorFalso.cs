namespace LastMileUY.DriverPWA.Tests;

using Microsoft.JSInterop;

// Hace de wwwroot/js/almacen.js: guarda los textos en memoria, para ver qué se le pide guardar y borrar
internal class NavegadorFalso : IJSRuntime
{
    public Dictionary<string, string> Datos { get; } = [];
    public int VecesBorradoTodo { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        object? resultado = null;

        switch (identifier)
        {
            case "lastMileAlmacen.guardar":
                Datos[(string)args![0]!] = (string)args[1]!;
                break;
            case "lastMileAlmacen.leer":
                resultado = Datos.GetValueOrDefault((string)args![0]!);
                break;
            case "lastMileAlmacen.borrar":
                Datos.Remove((string)args![0]!);
                break;
            case "lastMileAlmacen.borrarTodo":
                Datos.Clear();
                VecesBorradoTodo++;
                break;
            default:
                throw new InvalidOperationException($"Función inesperada: {identifier}");
        }

        return ValueTask.FromResult((TValue)resultado!);
    }
}
