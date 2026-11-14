using System.Globalization;
using System.Text;
using LastMileUY.API.Modules.Operadores.Domain;

namespace LastMileUY.API.Modules.Operadores.Application;

public static class CoberturaZonas
{
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return "";

        var sinTildes = new StringBuilder();

        foreach (var letra in texto.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letra) != UnicodeCategory.NonSpacingMark)
                sinTildes.Append(letra);
        }

        return string.Join(' ', sinTildes.ToString().ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static bool Cubre(Zona zona, string departamento, string ciudad)
    {
        if (zona.Departamento is null || Normalizar(zona.Departamento) != Normalizar(departamento))
            return false;

        if (zona.Localidades.Count == 0)
            return true;

        var buscada = Normalizar(ciudad);
        return zona.Localidades.Any(l => Normalizar(l) == buscada);
    }

    public static Zona? Elegir(IEnumerable<Zona> zonasActivas, string departamento, string ciudad) =>
        zonasActivas
            .Where(z => Cubre(z, departamento, ciudad))
            .OrderByDescending(z => z.Localidades.Count > 0)
            .ThenBy(z => z.Id)
            .FirstOrDefault();

    public static string? Superposicion(Zona zona, Zona otra)
    {
        if (zona.Departamento is null || otra.Departamento is null
            || Normalizar(zona.Departamento) != Normalizar(otra.Departamento))
            return null;

        if (zona.Localidades.Count == 0 && otra.Localidades.Count == 0)
            return $"La zona \"{otra.Nombre}\" ya cubre todo {otra.Departamento}.";

        var comunes = zona.Localidades
            .Where(l => otra.Localidades.Any(o => Normalizar(o) == Normalizar(l)))
            .ToList();

        return comunes.Count > 0
            ? $"La zona \"{otra.Nombre}\" ya cubre {string.Join(", ", comunes)}."
            : null;
    }
}
