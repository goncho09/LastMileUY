namespace LastMileUY.E2E.Tests;

using Microsoft.Playwright;

// Cuánto esperan las pruebas de navegador. Con la máquina cargada (la suite completa corre en paralelo
// con las pruebas de la API y del worker, que levantan contenedores) los valores de Playwright
// (5 s para cada Expect, 30 s para navegar) se quedaban cortos y alguna prueba fallaba sin motivo.
// Se espera más, pero solo cuando hace falta: si la condición se cumple antes, sigue enseguida.
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
