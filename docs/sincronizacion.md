# Sincronización de la app del repartidor

El repartidor trabaja con la hoja de ruta descargada (ver [hojas-de-ruta.md](hojas-de-ruta.md)) y
puede quedarse sin señal. La app registra cada entrega o intento fallido en el celular y, al
recuperar conexión, los manda a la API. Este documento explica qué hace la API con cada operación.

Código: `src/LastMileUY.API/Modules/Sincronizacion` (`SincronizarEntregasService`,
`ConsultasSincronizacionService`, `SincronizacionEndpoints`). Pruebas:
`tests/LastMileUY.API.Tests/Sincronizacion` y
`tests/LastMileUY.API.Tests/PuntaAPunta/SincronizacionDePuntaAPuntaTests.cs`.

## Qué manda la app

`POST /api/sincronizacion` (perfil Repartidor), con hasta **10 operaciones** por lote, en el orden en
que pasaron. Cada operación lleva:

| Campo | Para qué |
|---|---|
| `IdOperacion` | Un Guid que genera la app al registrar la operación. Se repite igual en cada reenvío. |
| `Tipo` | `Entrega` o `IntentoFallido`. |
| `RutaId`, `EnvioId` | Qué parada de qué ruta. |
| `FechaHoraDispositivo`, `Latitud`, `Longitud` | Cuándo y dónde pasó, según el celular. |
| `MotivoNoEntregaId`, `Observaciones` | Intento fallido: el motivo (de `GET /api/sincronizacion/motivos`) y un comentario. |
| `NombreReceptor`, `DocumentoReceptor`, `FirmaBase64`, `FotoBase64` | Entrega: la evidencia. Las imágenes son PNG o JPEG de hasta 1 MB. |

## Qué responde

Siempre `200`, con **un resultado por operación**, en el mismo orden. Solo responde `400` si el lote
viene vacío o trae más de 10 operaciones.

| Resultado | Significado | Qué hace la app |
|---|---|---|
| `Aplicada` | El envío cambió de estado. | La marca como sincronizada. |
| `Rechazada` | No corresponde y nunca se va a aplicar (falta evidencia, ruta de otro repartidor, fecha en el futuro, imagen inválida...). El mensaje dice por qué. | La muestra como rechazada; el repartidor avisa al despachador. |
| `Conflicto` | El envío ya no está como lo vio la app (ver política abajo). | La muestra como conflicto. |
| `Error` | Algo momentáneo (otro cambio a la vez, no se pudo guardar). `Reintentar` viene en `true`. | La vuelve a mandar más tarde, con el mismo `IdOperacion`. |

`Duplicada: true` indica que esa operación ya había llegado antes (ver abajo). `EstadoEnvio` dice
cómo quedó el envío en el servidor.

Cada operación se procesa y se guarda por separado: una rechazada no frena a las demás del lote.

## Sin duplicados al reenviar

La app puede mandar dos veces lo mismo: por ejemplo, si se corta la conexión después de que la API
guardó pero antes de que llegue la respuesta. Para que eso no genere dos entregas:

1. Toda operación que llega se guarda en `OperacionesSincronizadas` con su `IdOperacion` y su
   resultado, **en la misma transacción** que el cambio del envío.
2. Si llega una operación con un `IdOperacion` que ya está guardado, no se vuelve a aplicar: se
   responde el resultado de la primera vez, con `Duplicada: true`.
3. La base tiene un índice único `(OperadorId, IdOperacion)`. Si dos reenvíos llegan exactamente a la
   vez, uno se guarda y el otro choca con el índice y responde `Error` (reintentar), que en el
   siguiente intento ya da `Duplicada`.

## Reglas que se validan

Son las mismas que en el Backoffice, porque el cambio de estado pasa por `Envio.CambiarEstado`:

- La ruta tiene que existir, ser del operador (si no, para la API "no existe", por el aislamiento),
  ser **del repartidor que sincroniza** y estar Despachada o En curso.
- El envío tiene que ser una parada de esa ruta y estar En tránsito.
- **Evidencia según la regla operativa** del operador para la modalidad del envío:
  - `RequiereFirma`: tiene que venir la firma;
  - `RequiereFotografia`: tiene que venir una foto;
  - `RequiereDocumentoReceptor`: tienen que venir el nombre y el documento de quien recibe.

  Por ejemplo, con los datos de prueba, el operador 1 pide firma y el operador 2 pide foto y documento.
- Intento fallido: el motivo tiene que existir y estar activo.
- La fecha del celular no puede estar más de 5 minutos en el futuro, y la ubicación tiene que ser válida.

Una entrega aplicada genera el intento de entrega (con su número), la prueba de entrega con la
evidencia, y el evento en el historial con la **hora y la ubicación del celular**, no las de llegada.

## Política de conflictos: manda el servidor

Mientras el repartidor está sin conexión, alguien puede cambiar el mismo envío desde el Backoffice
(devolverlo al depósito, marcarlo extraviado), o la app puede haber mandado antes otra operación sobre
el mismo envío.

**Regla:** si cuando llega la operación el envío ya no está En tránsito, o la ruta ya no está
Despachada ni En curso, **la operación no se aplica** y queda registrada como `Conflicto`, con el
estado que tiene el envío en el servidor. No se pisa lo que ya está guardado.

Por qué esta política y no "gana el último":

- El servidor es el único que ve todo: lo que hizo el Backoffice y lo que mandaron otros dispositivos.
- Un envío entregado no puede "desentregarse" por una operación que llegó tarde.
- Los conflictos son raros y quedan visibles en el Backoffice con su motivo, para que el despachador
  decida (por ejemplo, volver a asignar el envío a una ruta).

## La ruta avanza sola

- Con la primera operación aplicada, la ruta pasa de Despachada a **En curso**. Su fecha de inicio es
  la de la primera operación según el celular.
- Cuando ninguno de sus envíos sigue En tránsito (todos entregados o con intento fallido), pasa a
  **Finalizada**.

## Evidencia

Las imágenes se guardan como archivos en la carpeta de `Evidencias:Directorio` (en Docker, el
volumen `evidencias_api`). En la base solo queda la clave del archivo, que arma la API
(`{operador}/{guid}.png`): la app nunca elige el nombre, así que no se puede usar para leer otro
archivo del servidor. Antes de guardarla se verifica que sea realmente PNG o JPEG mirando sus
primeros bytes.

## Endpoints

| Método y ruta | Perfiles |
|---|---|
| `POST /api/sincronizacion` | Repartidor |
| `GET /api/sincronizacion/motivos` | Repartidor, Operador, Despachador |
| `GET /api/sincronizacion/operaciones?rutaId=&resultado=&pagina=` (historial) | Operador, Despachador |
| `GET /api/sincronizacion/evidencias/{intentoId}/{firma\|foto}` | Operador, Despachador |
