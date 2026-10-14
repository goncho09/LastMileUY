namespace LastMileUY.API.Modules.Envios.Domain;

// Por dónde entró el envío. Lo decide la API según quién lo crea, nunca lo manda el cliente.
public enum CanalIngreso
{
    // Integración o personal del operador usando la API directamente
    Api = 1,

    // Un usuario de comercio desde el portal del comercio
    Portal = 2
}
