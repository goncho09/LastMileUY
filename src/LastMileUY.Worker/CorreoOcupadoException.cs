namespace LastMileUY.Worker;

public sealed class CorreoOcupadoException(string mensaje)
    : Exception(mensaje);