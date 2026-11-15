using System.Globalization;
using System.Security.Claims;
using System.Text;
using LastMileUY.API.Infrastructure.Multitenancy;
using LastMileUY.API.Infrastructure.Persistence;
using LastMileUY.API.Modules.Destinatarios.Application;
using LastMileUY.Contracts.Destinatarios;
using LastMileUY.Contracts.Envios;
using LastMileUY.Contracts.Integraciones;
using Microsoft.EntityFrameworkCore;

namespace LastMileUY.API.Modules.Integraciones.Application;

public class ImportarArchivoService(
    ApplicationDbContext db,
    AltaDestinatariosService destinatarios,
    ImportarEnviosService importar,
    IContextoOperador contexto)
{
    public const int MaximoFilas = 500;
    public const int MaximoCaracteres = 1_000_000;

    public async Task<ImportarEnviosResponse> ImportarAsync(
        ImportarArchivoRequest request, ClaimsPrincipal usuario, CancellationToken ct = default)
    {
        var comercioId = contexto.ComercioId
            ?? throw new UnauthorizedAccessException("La importación de archivos es para los usuarios de un comercio.");

        var filas = LeerFilas(request.Contenido);
        var resultados = new SortedDictionary<int, ResultadoImportacionDto>();
        var lote = new List<(int Linea, EnvioApiRequest Envio)>();

        foreach (var (linea, valores) in filas)
        {
            var referencia = valores.GetValueOrDefault(ColumnasImportacion.Referencia);

            try
            {
                if (string.IsNullOrWhiteSpace(referencia))
                    throw new ArgumentException("Falta la referencia del envío.");

                referencia = referencia.Trim();

                if (await db.Envios.AsNoTracking().AnyAsync(e => e.ReferenciaExterna == referencia, ct))
                {
                    lote.Add((linea, new EnvioApiRequest { ReferenciaExterna = referencia }));
                    continue;
                }

                var bultos = Bultos(valores);
                var (destinatarioId, direccionId) = await DestinatarioAsync(comercioId, valores, ct);

                lote.Add((linea, new EnvioApiRequest
                {
                    ReferenciaExterna = referencia,
                    DestinatarioId = destinatarioId,
                    DireccionId = direccionId,
                    Modalidad = Opcional(valores, ColumnasImportacion.Modalidad),
                    Bultos = bultos
                }));
            }
            catch (ArgumentException ex)
            {
                resultados[linea] = new ResultadoImportacionDto(linea, referencia?.Trim(), ResultadosImportacion.Rechazado,
                    null, null, ex.Message);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        foreach (var parte in lote.Chunk(ImportarEnviosService.MaximoPorLote))
        {
            var respuesta = await importar.ImportarAsync(
                new ImportarEnviosRequest { Envios = parte.Select(p => p.Envio).ToList() }, usuario, ct);

            foreach (var resultado in respuesta.Resultados)
            {
                var linea = parte[resultado.Indice].Linea;
                resultados[linea] = resultado with { Indice = linea };
            }
        }

        var lista = resultados.Values.ToList();

        return new ImportarEnviosResponse(
            lista.Count(r => r.Resultado == ResultadosImportacion.Creado),
            lista.Count(r => r.Resultado == ResultadosImportacion.Existente),
            lista.Count(r => r.Resultado == ResultadosImportacion.Rechazado),
            lista);
    }

    private async Task<(int DestinatarioId, int DireccionId)> DestinatarioAsync(
        int comercioId, IReadOnlyDictionary<string, string> valores, CancellationToken ct)
    {
        var nombre = Opcional(valores, ColumnasImportacion.Nombre);
        var apellido = Opcional(valores, ColumnasImportacion.Apellido);
        var email = Opcional(valores, ColumnasImportacion.Email)?.ToLowerInvariant();
        var telefono = Opcional(valores, ColumnasImportacion.Telefono);

        var direccion = new CrearDireccionRequest
        {
            Calle = Opcional(valores, ColumnasImportacion.Calle) ?? "",
            Numero = Opcional(valores, ColumnasImportacion.Numero) ?? "",
            Apartamento = Opcional(valores, ColumnasImportacion.Apartamento),
            Ciudad = Opcional(valores, ColumnasImportacion.Ciudad) ?? "",
            Departamento = Opcional(valores, ColumnasImportacion.Departamento) ?? ""
        };

        var candidatos = db.Destinatarios.AsNoTracking().Include(d => d.Direcciones).Where(d => d.ComercioId == comercioId);

        var existente = email is not null
            ? await candidatos.FirstOrDefaultAsync(d => d.Email == email, ct)
            : await candidatos.FirstOrDefaultAsync(d => d.Nombre == nombre && d.Apellido == apellido && d.Telefono == telefono, ct);

        if (existente is null)
        {
            var creado = await destinatarios.CrearAsync(new CrearDestinatarioRequest
            {
                ComercioId = comercioId,
                Nombre = nombre ?? "",
                Apellido = apellido ?? "",
                Email = email,
                Telefono = telefono,
                Direccion = direccion
            }, ct);

            return (creado.Id, creado.Direcciones[0].Id);
        }

        var misma = existente.Direcciones.FirstOrDefault(d =>
            d.Activa
            && Igual(d.Calle, direccion.Calle)
            && Igual(d.Numero, direccion.Numero)
            && Igual(d.Apartamento, direccion.Apartamento)
            && Igual(d.Ciudad, direccion.Ciudad));

        if (misma is not null)
            return (existente.Id, misma.Id);

        var agregada = await destinatarios.AgregarDireccionAsync(existente.Id, direccion, ct)
            ?? throw new ArgumentException("No se encontró el destinatario para agregarle la dirección.");

        return (existente.Id, agregada.Id);
    }

    private static List<CrearBultoRequest> Bultos(IReadOnlyDictionary<string, string> valores)
    {
        var cantidadTexto = Opcional(valores, ColumnasImportacion.Bultos);
        var cantidad = 1;

        if (cantidadTexto is not null && (!int.TryParse(cantidadTexto, out cantidad) || cantidad is < 1 or > 20))
            throw new ArgumentException("La cantidad de bultos va de 1 a 20.");

        var peso = Numero(valores, ColumnasImportacion.PesoKg, "el peso");
        var largo = Numero(valores, ColumnasImportacion.LargoCm, "el largo");
        var ancho = Numero(valores, ColumnasImportacion.AnchoCm, "el ancho");
        var alto = Numero(valores, ColumnasImportacion.AltoCm, "el alto");
        var descripcion = Opcional(valores, ColumnasImportacion.Descripcion);

        return Enumerable.Range(1, cantidad)
            .Select(_ => new CrearBultoRequest
            {
                PesoKg = peso,
                LargoCm = largo,
                AnchoCm = ancho,
                AltoCm = alto,
                Descripcion = descripcion
            })
            .ToList();
    }

    private static decimal Numero(IReadOnlyDictionary<string, string> valores, string columna, string campo)
    {
        var texto = Opcional(valores, columna)?.Replace(',', '.');

        if (texto is null || !decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero) || numero <= 0)
            throw new ArgumentException($"Revisá {campo}: tiene que ser un número mayor que cero.");

        return numero;
    }

    private static string? Opcional(IReadOnlyDictionary<string, string> valores, string columna) =>
        valores.TryGetValue(columna, out var valor) && !string.IsNullOrWhiteSpace(valor) ? valor.Trim() : null;

    private static bool Igual(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    public static List<(int Linea, Dictionary<string, string> Valores)> LeerFilas(string? contenido)
    {
        if (string.IsNullOrWhiteSpace(contenido))
            throw new ArgumentException("El archivo está vacío.");

        if (contenido.Length > MaximoCaracteres)
            throw new ArgumentException("El archivo es demasiado grande: partilo en varios.");

        var lineas = contenido.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var primera = Array.FindIndex(lineas, l => !string.IsNullOrWhiteSpace(l));
        var separador = lineas[primera].Contains(';') ? ';' : ',';
        var encabezado = Separar(lineas[primera], separador).Select(Columna).ToList();

        var faltan = ColumnasImportacion.Obligatorias.Where(c => !encabezado.Contains(c)).ToList();

        if (faltan.Count > 0)
            throw new ArgumentException($"Al archivo le faltan columnas: {string.Join(", ", faltan)}. Descargá la plantilla.");

        var filas = new List<(int, Dictionary<string, string>)>();

        for (var i = primera + 1; i < lineas.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lineas[i]))
                continue;

            var valores = Separar(lineas[i], separador);
            var fila = new Dictionary<string, string>();

            for (var c = 0; c < encabezado.Count && c < valores.Count; c++)
                fila[encabezado[c]] = valores[c];

            filas.Add((i + 1, fila));
        }

        if (filas.Count == 0)
            throw new ArgumentException("El archivo no tiene envíos, solo el encabezado.");

        if (filas.Count > MaximoFilas)
            throw new ArgumentException($"Un archivo puede tener como máximo {MaximoFilas} envíos; partilo en varios.");

        return filas;
    }

    private static string Columna(string encabezado) =>
        new string(encabezado.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray())
            .Replace(' ', '_');

    private static List<string> Separar(string linea, char separador)
    {
        var valores = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;

        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];

            if (entreComillas)
            {
                if (c == '"' && i + 1 < linea.Length && linea[i + 1] == '"')
                {
                    actual.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreComillas = false;
                }
                else
                {
                    actual.Append(c);
                }
            }
            else if (c == '"')
            {
                entreComillas = true;
            }
            else if (c == separador)
            {
                valores.Add(actual.ToString());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }

        valores.Add(actual.ToString());
        return valores;
    }
}
