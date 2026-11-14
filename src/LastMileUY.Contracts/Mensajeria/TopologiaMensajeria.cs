namespace LastMileUY.Contracts.Mensajeria;

public static class TopologiaMensajeria
{
    public const string ExchangeEnvios = "lastmileuy.envios";

    public const string ColaWorker = "lastmileuy.worker.envios";

    public const string PatronEventosEnvio = "envio.#";

    public const string PatronEventosBulto = "bulto.#";

    public const string EncabezadoCorrelacion = "X-Correlation-Id";

    public const string ColaCorreos = "lastmileuy.correos";

    public static readonly string[] PatronesCorreos = [TiposEvento.EnvioAdmitido, TiposEvento.EnvioEstadoCambiado];
}
