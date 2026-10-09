namespace LastMileUY.API.Modules.Envios.Domain;

public enum EstadoEnvio
{
    Admitido = 1,
    EnDeposito = 2,
    AsignadoARuta = 3,
    EnTransito = 4,
    Entregado = 5,
    NoEntregado = 6,
    Reprogramado = 7,
    EnDevolucion = 8,
    Devuelto = 9,
    Extraviado = 10
}