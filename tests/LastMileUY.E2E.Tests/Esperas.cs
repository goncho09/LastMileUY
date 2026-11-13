namespace LastMileUY.E2E.Tests;

using Microsoft.Playwright;

public static class Esperas
{
    public static void Configurar() => Assertions.SetDefaultExpectTimeout(15_000);

    public static IBrowserContext Preparar(IBrowserContext contexto)
    {
        contexto.SetDefaultTimeout(30_000);
        contexto.SetDefaultNavigationTimeout(60_000);
        return contexto;
    }
}
