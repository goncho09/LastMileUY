
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LastMileUY.API.Tests.Arquitectura;

public class DependenciasDelDominioTests
{
    [Fact]
    public void El_dominio_no_debe_depender_de_infraestructura_ni_de_aplicacion()
    {
        var raiz = BuscarRaizRepositorio();
        var modulos = Path.Combine(raiz, "src", "LastMileUY.API", "Modules");

        Assert.True(Directory.Exists(modulos),
            $"No existe la carpeta de módulos: {modulos}");

        var archivos = Directory
            .EnumerateFiles(modulos, "*.cs", SearchOption.AllDirectories)
            .Where(ruta => ruta.Replace('\\', '/')
                .Contains("/Domain/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(ruta => ruta)
            .ToArray();

        Assert.NotEmpty(archivos);

        var errores = new List<string>();

        foreach (var archivo in archivos)
        {
            var arbol = CSharpSyntaxTree.ParseText(
                File.ReadAllText(archivo));

            var sintaxis = arbol.GetRoot();

            // Analizar referencias del código, ignorando comentarios.
            var referencias = sintaxis.DescendantNodes()
                .OfType<NameSyntax>()
                .Select(nodo => nodo.ToString()
                    .Replace("global::", string.Empty, StringComparison.Ordinal))
                .Where(EsDependenciaProhibida)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(nombre => nombre);

            foreach (var referencia in referencias)
            {
                var relativo = Path.GetRelativePath(raiz, archivo);
                errores.Add($"{relativo}: {referencia}");
            }
        }

        Assert.True(errores.Count == 0,
            "El dominio contiene referencias a capas prohibidas:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errores));
    }

    private static bool EsDependenciaProhibida(string referencia)
    {
        // El dominio no puede utilizar EF Core.
        if (referencia.StartsWith(
                "Microsoft.EntityFrameworkCore",
                StringComparison.Ordinal))
            return true;

        // Tampoco la infraestructura global de la API.
        if (referencia.StartsWith(
                "LastMileUY.API.Infrastructure",
                StringComparison.Ordinal))
            return true;

        // Ni las capas Application/Infrastructure de los módulos.
        const string prefijo = "LastMileUY.API.Modules.";

        if (!referencia.StartsWith(prefijo, StringComparison.Ordinal))
            return false;

        var resto = referencia[prefijo.Length..];
        var segmentos = resto.Split('.');

        return segmentos.Length >= 2 &&
               (segmentos[1] == "Infrastructure" ||
                segmentos[1] == "Application");
    }

    private static string BuscarRaizRepositorio()
    {
        for (DirectoryInfo? carpeta = new(AppContext.BaseDirectory);
             carpeta is not null;
             carpeta = carpeta.Parent)
        {
            if (File.Exists(Path.Combine(
                    carpeta.FullName, "LastMileUY.slnx")))
                return carpeta.FullName;
        }

        throw new DirectoryNotFoundException(
            "No se encontró LastMileUY.slnx desde el directorio de pruebas.");
    }
}
