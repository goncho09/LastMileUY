# Sincronización de la app del repartidor

El repartidor trabaja con la hoja de ruta descargada (ver [hojas-de-ruta.md](hojas-de-ruta.md)) y
puede quedarse sin señal. La app registra cada entrega o intento fallido en el celular y, al
recuperar conexión, los manda a la API. Este documento explica qué hace la API con cada operación.

**Es la única forma de registrar entregas e intentos fallidos.** La app usa este mismo endpoint
también cuando tiene conexión (manda una operación por vez): así la evidencia, los duplicados y los
conflictos se controlan en un solo lugar, y el Backoffice ve todo lo que hizo el repartidor.

Código: `src/LastMileUY.API/Modules/Sincronizacion` (`SincronizarEntregasService`,
`ConsultasSincronizacionService`, `SincronizacionEndpoints`). En la app:
`src/LastMileUY.DriverPWA/Services/SincronizacionService.cs`. Pruebas:
`tests/LastMileUY.API.Tests/Sincronizacion`,
`tests/LastMileUY.API.Tests/PuntaAPunta/SincronizacionDePuntaAPuntaTests.cs` y
`tests/LastMileUY.DriverPWA.Tests`.

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
  La hoja de ruta trae lo que pide cada parada (`Evidencia`), para que la app lo pida antes de mandar.
- Intento fallido: el motivo tiene que existir y estar activo.
- La fecha del celular no puede estar más de 5 minutos en el futuro, y la ubicación tiene que ser válida.

Una entrega aplicada genera el intento de entrega (con su número), la prueba de entrega con la
evidencia, y el evento en el historial con la **hora y la ubicación del celular**, no las de llegada.

## Reglas de entrega parcial: qué versión vale

Cada operador define si permite entregas parciales y qué pasa con los bultos que quedan sin entregar:
vuelven a salir en otra hoja de ruta o vuelven al comercio. Lo configura el perfil Operador en
"Mi operador" del Backoffice.

**Las reglas son versionadas.** Cada cambio publica una versión nueva (1, 2, 3...) con quién la
publicó, cuándo y por qué. Las anteriores no se modifican. Lo hace cumplir la base: el usuario de la
aplicación (`lastmileuy_app`) solo puede leer e insertar en `ReglasEntregaParcial`, no actualizar ni
borrar. Por eso siempre se puede saber con qué reglas se evaluó una entrega.

**Regla: vale la versión con la que salió la hoja de ruta.** Al despachar, la ruta guarda la versión
vigente (`Rutas.ReglaEntregaParcialId`) y la conserva hasta el final. Si el operador publica otra
mientras el repartidor está en la calle, esa ruta sigue con la suya; las rutas que se despachen
después usan la nueva.

Por qué esta y no "la vigente al sincronizar" o "la vigente según la hora del celular":

- **Es justa con el repartidor:** se lo evalúa con las reglas que descargó con su hoja de ruta, no con
  unas que se publicaron mientras estaba sin conexión.
- **No depende del reloj del celular:** si valiera la hora del dispositivo, atrasarla alcanzaría para
  caer en una versión más permisiva.
- **Es una sola respuesta para toda la ruta:** todas sus paradas se evalúan igual.

Qué se aplica hoy:

- Una `EntregaParcial` se **rechaza** si la versión de la ruta no permite entregas parciales. No se
  reintenta: con esas reglas nunca va a valer.
- Si la ruta salió sin versión (el operador todavía no había publicado ninguna), se permite, como antes
  de que existieran las versiones.
- Cada operación sincronizada guarda con qué versión se evaluó (`OperacionesSincronizadas.ReglaEntregaParcialId`),
  y el historial del Backoffice la muestra.

### Qué pasa con los bultos que quedan

Si una operación deja el envío entregado parcialmente, en la misma transacción se aplica el destino de
la versión de la ruta (`Envio.AplicarPoliticaBultosPendientes`):

| Destino | El envío pasa a | Después |
|---|---|---|
| `NuevaHojaDeRuta` | Reprogramado | Puede salir en otra hoja de ruta, solo con los bultos pendientes (ver [hojas-de-ruta.md](hojas-de-ruta.md)) |
| `NuevaHojaDeRuta`, sin intentos disponibles en la regla operativa | En devolución | Manda el límite de intentos que ya existía: no se reprograma sin fin |
| `Devolucion` | En devolución | Al pasar a Devuelto, los bultos sin entregar quedan Devuelto; los entregados no cambian |

- **Sin versión en la ruta:** el envío queda entregado parcialmente y lo decide el despachador.
- **En el historial:** el cambio queda como evento con origen "Sistema" y la versión aplicada, por ejemplo
  "Bultos pendientes: vuelven a salir en otra hoja de ruta (reglas de entrega parcial v3)". La respuesta
  a la app también lo dice.
- **Avisos:** el comercio se entera por los avisos de siempre (outbox).

Una entrega parcial cuenta como intento sin éxito, porque no se entregó todo. Con la regla de prueba del
operador 1, que permite un solo intento, lo pendiente siempre va a devolución.

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
| `GET /api/sincronizacion/mias?resultado=&pagina=` (las propias, para "Mis operaciones" de la app) | Repartidor |
| `GET /api/sincronizacion/evidencias/{intentoId}/{firma\|foto}` | Operador, Despachador |
| `GET /api/operador/reglas-entrega-parcial` (todas las versiones, la más nueva primero) | Operador |
| `POST /api/operador/reglas-entrega-parcial` (publicar una versión nueva) | Operador |
