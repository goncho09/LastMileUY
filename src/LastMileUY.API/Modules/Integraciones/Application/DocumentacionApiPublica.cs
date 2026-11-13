using Microsoft.AspNetCore.OpenApi;
using LastMileUY.API.Modules.Integraciones.Endpoints;
using Microsoft.OpenApi;

namespace LastMileUY.API.Modules.Integraciones.Application;

public static class DocumentacionApiPublica
{
    public static void Configurar(OpenApiOptions opciones)
    {
        opciones.ShouldInclude = descripcion => descripcion.GroupName == ApiPublicaV1Endpoints.Version;

        opciones.AddDocumentTransformer((documento, _, _) =>
        {
            documento.Info = new OpenApiInfo
            {
                Title = "LastMileUY · API pública",
                Version = "v1",
                Description = """
                    API para que el sistema de un comercio (tienda online, ERP) cargue y siga sus envíos.

                    **Autenticación.** Cada pedido lleva una clave en el encabezado `X-Api-Key`. Las claves
                    las crea el comercio en el portal (menú *Integración*): cada una vale para el comercio y
                    el operador logístico con el que se creó, y se puede revocar en cualquier momento.

                    **Versiones.** Todo está bajo `/api/v1`. Un cambio incompatible saldría en `/api/v2`, y
                    la v1 seguiría funcionando.

                    **Ambiente de pruebas.** Las claves de prueba (empiezan con `lmuy_test_`) usan los mismos
                    endpoints, pero trabajan con datos aislados: los envíos y destinatarios que crean no llegan
                    al operador logístico ni se mezclan con los de verdad. Como en pruebas nadie mueve los
                    envíos, `POST /api/v1/pruebas/envios/{id}/simular` los hace avanzar de estado.

                    **Límites.** Por clave, por minuto: 120 pedidos y 10 lotes con una clave de producción, y la
                    mitad (60 y 5) con una de prueba. Al pasarlos la API responde 429 con el encabezado
                    `Retry-After` (segundos a esperar).

                    **Importación sin duplicados.** `POST /api/v1/envios/lote` usa la referencia externa
                    (el número de pedido del comercio) para no crear el mismo envío dos veces: se puede
                    reenviar el lote completo o una parte sin miedo.
                    """.ReplaceLineEndings("\n")
            };

            documento.Components ??= new OpenApiComponents();
            documento.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            documento.Components.SecuritySchemes[AutenticacionClaveApi.Esquema] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = AutenticacionClaveApi.Encabezado,
                Description = "Clave de API del comercio: lmuy_live_… (producción) o lmuy_test_… (ambiente de pruebas). Se crea en el portal."
            };

            documento.Security ??= [];
            documento.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(AutenticacionClaveApi.Esquema, documento)] = []
            });

            return Task.CompletedTask;
        });
    }
}
