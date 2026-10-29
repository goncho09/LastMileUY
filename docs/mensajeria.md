# Mensajería: eventos de envío con RabbitMQ

Cada vez que un envío entra al sistema o cambia de estado, la API publica un evento en RabbitMQ. El
worker (`LastMileUY.Worker`) lo consume y, a partir de él, se hacen los avisos a los comercios. Este
documento dice qué lleva cada evento, cómo se garantiza que no se pierda y cómo se sigue un cambio de
punta a punta.

Código:
- Contrato: `src/LastMileUY.Contracts/Mensajeria` (lo comparten la API y el worker).
- API: `src/LastMileUY.API/Infrastructure/Mensajeria` (outbox y publicador),
  `Infrastructure/Correlacion` y la migración `Outbox`.
- Worker: `src/LastMileUY.Worker` (`ConsumidorEventosEnvio`, `ProcesadorAvisos`, `DespachadorAvisos` y
  `ClienteAvisos`), `src/LastMileUY.Contracts/Avisos` y la migración
  `MensajesProcesados`.

Pruebas: `tests/LastMileUY.API.Tests/Mensajeria` (outbox y publicación) y `tests/LastMileUY.Worker.Tests`
(procesamiento, repetidos, reintentos y envío de avisos).

## El recorrido

```
Backoffice / portal / app del repartidor
        │  HTTP (X-Correlation-Id)
        ▼
API ── guarda el cambio del envío + el evento en MensajesOutbox (una sola transacción)
        │
PublicadorOutbox ── lee MensajesOutbox, publica y marca como publicado
        │  exchange "lastmileuy.envios" (topic), clave = tipo de evento
        ▼
cola "lastmileuy.worker.envios" ──► worker
```

## El contrato: `EstadoEnvioCambiado`

Un solo tipo de mensaje, en JSON, para todo lo que le pasa a un envío.

| Campo | Qué es |
|---|---|
| `EventoId` | Guid único del evento. También va en la propiedad `MessageId` del mensaje. |
| `Tipo` | `envio.admitido` (entró al sistema) o `envio.estado_cambiado`. Es la clave de ruteo. |
| `Version` | Versión del contrato (hoy 1). |
| `OcurridoEn` | Cuándo pasó (UTC). Si la app del repartidor registró sin conexión, es la hora del celular. |
| `OperadorId`, `ComercioId`, `EnvioId` | De quién es y cuál es. |
| `CodigoSeguimiento` | El código público del envío. |
| `Estado`, `EstadoAnterior` | Nombre del estado (`EstadoEnvio`). `EstadoAnterior` es null en `envio.admitido`. |
| `Origen` | Desde dónde se hizo: Backoffice, Repartidor, Portal, Api, Sistema... |
| `EsPublico` | false si el evento no se muestra en el seguimiento público. |
| `CorrelacionId` | El del pedido HTTP que originó el cambio. También va en la propiedad `CorrelationId`. |

**Sin datos personales ni notas internas.** El evento no lleva el destinatario, la dirección ni la
descripción del cambio de estado (puede ser una nota interna). Quien necesite más datos consulta el
envío con su operador, y le aplica el aislamiento del ADR-01.

**Versiones.** Agregar un campo no cambia la versión: quien no lo conoce lo ignora. Si se quita un
campo o cambia lo que significa, se sube `Version` y el worker tiene que aceptar las dos mientras
convivan.

## Outbox: que ningún evento se pierda ni sobre

Si la API guardara el cambio y después publicara en RabbitMQ, podría fallar en el medio: quedaría un
envío entregado sin evento (y el comercio sin aviso), o un evento de un cambio que no se guardó.

Por eso el evento no se publica en el momento: `OutboxInterceptor` lo agrega a la tabla
`MensajesOutbox` **en el mismo guardado** que el cambio del envío. O quedan los dos o ninguno. Funciona
para cualquier cambio de estado, venga de donde venga, porque todos pasan por `Envio.CambiarEstado`.

Después, `PublicadorOutbox` (un servicio en segundo plano de la API):
1. Toma los pendientes en orden (`FOR UPDATE SKIP LOCKED`: con varias instancias de la API, cada
   mensaje lo toma una sola).
2. Los publica con confirmación: espera a que RabbitMQ diga que lo guardó. Con `mandatory`, si no hay
   ninguna cola para recibirlo, RabbitMQ lo devuelve y queda pendiente en vez de perderse.
3. Lo marca como publicado. Si falla, anota el error y reintenta con espera creciente (hasta un minuto).

Publica apenas se guarda un evento (la API le avisa) y, además, revisa la tabla cada 5 segundos.

**Al menos una vez.** Si la API se cae entre publicar y marcar, el mensaje sale de nuevo. El worker
descarta los repetidos por `EventoId` (ver abajo).

## El worker

### Qué hace con cada evento

`ProcesadorAvisos` deja un aviso pendiente (`EntregasAvisos`, estado Pendiente) por cada suscripción
**activa** del comercio **a ese tipo de evento** (`SuscripcionesAvisos.TipoEvento` igual a `Tipo`). El
contenido del aviso es el evento tal cual, sin datos personales. Después los manda por HTTP (ver
"Envío de los avisos a los comercios").

Se conecta a la base con el **usuario de la aplicación** (`lastmileuy_app`) y, antes de nada, carga el
operador del evento (`app.operador_id`, solo para la transacción). Le aplica el RLS del ADR-01 igual que
a la API: solo ve las suscripciones de ese operador, aunque el comercio trabaje con varios.

### Sin procesar dos veces

RabbitMQ entrega "al menos una vez": el mismo evento puede llegar dos veces (reenvío del outbox, o el
worker se cae antes de confirmar). En la **misma transacción** en que registra los avisos, el worker
anota el evento en `MensajesProcesados` (clave: `EventoId` + nombre del proceso, con RLS por operador):

- Si la anotación entra, registra los avisos y confirma todo junto.
- Si ya estaba (`ON CONFLICT DO NOTHING` no inserta nada), es un repetido: no hace nada.
- Si algo falla a mitad, no queda ni la anotación ni los avisos, y el reintento empieza de cero.

### Reintentos con espera creciente

El mensaje se confirma (ack) recién cuando se procesó. Si procesarlo falla (por ejemplo, la base no
responde), el worker no lo descarta ni lo reintenta enseguida, sino que lo deja en una **cola de espera**:

| Intento que falla | Espera | Cola |
|---|---|---|
| 1 | 5 segundos | `lastmileuy.worker.envios.reintento.1` |
| 2 | 30 segundos | `lastmileuy.worker.envios.reintento.2` |
| 3 | 2 minutos | `lastmileuy.worker.envios.reintento.3` |
| 4 | — | `lastmileuy.worker.envios.fallidos` |

- Nadie consume las colas de espera. El mensaje tiene un vencimiento; al vencer, RabbitMQ lo devuelve a
  la cola principal (dead-letter) y se procesa de nuevo.
- Una cola por nivel, para que un mensaje que espera 2 minutos no frene a uno que espera 5 segundos.
- La copia lleva los encabezados `x-intentos` y `x-ultimo-error`, y conserva el `MessageId` y el
  `CorrelationId`. El original se confirma recién cuando RabbitMQ guardó la copia. Si no se puede dejar
  la copia, el original vuelve a la cola: ningún mensaje se pierde.
- Agotados los reintentos, el mensaje queda en la cola de **fallidos** para revisarlo y reintentarlo a
  mano. Un mensaje que no se puede leer (formato inválido o una versión más nueva del contrato) va
  directo ahí, porque reintentarlo no lo arregla.
- Las esperas se configuran en `Mensajeria:EsperasReintento` (las pruebas usan esperas cortas).

Ojo: estos son los reintentos de **procesar el evento** (RabbitMQ). Los de **mandar el aviso** al
comercio son otros y van en la base (ver abajo).

## Envío de los avisos a los comercios

`DespachadorAvisos` (en el worker) toma los avisos pendientes y se los manda a cada comercio:
`POST` a la `UrlDestino` de la suscripción, con el evento en JSON.

| Encabezado | Qué es |
|---|---|
| `X-Aviso-Id` | Identificador único del aviso (`EntregasAvisos.EventoId`). Si llega dos veces, el comercio descarta el repetido. |
| `X-Aviso-Tipo` | Tipo de evento: `envio.admitido` o `envio.estado_cambiado`. |
| `X-Aviso-Intento` | Número de intento: 1 la primera vez. |
| `X-Correlation-Id` | El del pedido HTTP que originó el cambio. |

Los nombres están en `Contracts/Avisos/EncabezadosAvisos`.

**Cómo los toma.**
- Recorre los operadores y, para cada uno, carga su `app.operador_id` antes de leer, igual que
  `ProcesadorAvisos`: el RLS del ADR-01 se le aplica, sin un usuario que lo saltee.
- Toma los avisos de a uno (`FOR UPDATE SKIP LOCKED`): con varias instancias del worker, cada aviso lo
  manda una sola. Primero los más viejos.
- Toma los que están en estado Pendiente y cuya `FechaProximoIntento` ya pasó o está vacía.
- Manda apenas `ProcesadorAvisos` registra avisos (le avisa) y, además, revisa cada 5 segundos.

**Qué anota en `EntregasAvisos`.**
- Respuesta 2xx: queda **Entregado** (2), con `FechaEntrega`.
- Cualquier otra cosa queda **Fallido** (3), con el motivo en `UltimoError`: otro código, sin respuesta,
  más de 10 segundos, o destino no permitido.
- En los dos casos suma `CantidadIntentos` y anota `FechaUltimoIntento`.
- Si el comercio desactivó la suscripción después de registrarse el aviso, no se manda y queda Fallido.
- No sigue redirecciones.

**URLs permitidas (que no sirva para entrar a la red interna, SSRF).** La URL la carga el comercio, pero
la llama el worker desde adentro. Por eso `Contracts/Avisos/DestinosAvisos` solo acepta:
- `https`, sin usuario ni contraseña en la URL;
- direcciones públicas: ni `localhost`, ni redes privadas (10.x, 172.16–31.x, 192.168.x), ni
  169.254.169.254, la dirección de metadatos de la nube.

El worker lo revisa **al conectarse**, con la IP a la que resolvió el nombre. Revisar solo la URL no
alcanza: `avisos.comercio.com` puede resolver a una IP interna.

Para desarrollo, `Avisos:PermitirDestinosLocales` en `true` deja usar `http` y direcciones internas. Así
se puede usar el receptor de prueba. En producción va en `false`, que es el valor por defecto.

### Receptor de prueba

`LastMileUY.ReceptorAvisos` hace de comercio. En compose está en http://localhost:8085 y el worker lo
ve como `http://lastmileuy.receptoravisos:8080`.

- `POST /avisos/...`: recibe el aviso y responde 200. La ruta después de `/avisos` es libre, para
  distinguir suscripciones.
- Para probar fallos: `?estado=503` responde ese código y `?demora=15` tarda esos segundos.
- `GET /avisos`: lo recibido, con los encabezados y el contenido, el último primero (guarda hasta 200).
- `DELETE /avisos`: lo borra.

Para probarlo, en el portal del comercio (menú **Avisos**) cargá una suscripción con la URL
`http://lastmileuy.receptoravisos:8080/avisos/mi-comercio` y cambiale el estado a un envío de ese
comercio.

### Dónde sigue cada tarjeta

- **Firmar los avisos:** `ClienteAvisos.ArmarPedido`, donde está el comentario. El receptor guarda todos
  los encabezados `X-...`, así que se puede verificar la firma ahí.
- **Historial de intentos:** `DespachadorAvisos.EnviarSiguienteAsync`, en la misma transacción en que se
  anota el resultado. `ResultadoEnvio` trae el código HTTP, el error y la duración.
- **Reintentos con espera creciente:** `DespachadorAvisos.ProximoIntento`. Hoy devuelve null, que
  significa que el aviso no se reintenta y queda Fallido. Si devuelve una fecha, el aviso sigue
  Pendiente y el despachador lo vuelve a tomar cuando llega esa fecha. Ya hay una prueba de eso.
- **Portal (suscripciones y fallidos):**
  - suscripciones: hecho. `GET`, `POST` y `PATCH /api/avisos/suscripciones` (rol Comercio) y la página
    **Avisos** del portal. La URL se valida con `DestinosAvisos.Validar`; en la API, `Avisos:PermitirDestinosLocales`
    deja usar el receptor de prueba (compose lo pone en `true`). No se borran: se desactivan;
  - los fallidos son `EntregasAvisos` con estado 3, y el motivo está en `UltimoError`;
  - para reenviar uno, se vuelve a estado 1 con `FechaProximoIntento` vacía.

## El tablero en vivo del backoffice

El backoffice también escucha los eventos (`SignalR/ConsumidorEventosEnvio`) y avisa por SignalR a
las pantallas abiertas de ese operador (un grupo por operador en `EnviosHub`). El aviso no lleva los
datos a mostrar: la pantalla vuelve a consultar la API, igual que al reconectarse.

A diferencia del worker, no necesita que el mensaje llegue sí o sí:
- Cada instancia del backoffice usa una **cola propia y temporal** (nombre que elige RabbitMQ, se
  borra al cerrar la conexión). Con varias instancias, todas reciben todos los eventos; con el
  backoffice apagado, no se acumulan eventos viejos.
- Si no puede avisar un evento, lo descarta en vez de devolverlo a la cola: perderlo solo atrasa la
  pantalla hasta el siguiente.

## Permisos en la base

`MensajesOutbox` tiene eventos de todos los operadores y no lleva RLS. En cambio, cada usuario de la
base puede hacer solo lo que le toca:

| Usuario | Puede |
|---|---|
| `lastmileuy_app` (la API) | Solo insertar: escribe el evento junto con el cambio del envío, pero no puede leer ninguno, ni los de su propio operador. |
| `lastmileuy_publicador` (el publicador) | Solo leer y marcar el outbox. No tiene acceso a ninguna otra tabla. |

Así, un error en la API no puede mostrarle a un operador los eventos de otro, y el publicador no puede
ver los envíos.

## Correlación

Cada pedido a la API tiene un identificador de correlación:
- Si el pedido trae el encabezado `X-Correlation-Id` (letras, números, `.`, `_` o `-`, hasta 64), se usa
  ese. Si no, la API genera uno.
- La API lo devuelve en la respuesta, en el mismo encabezado.
- Los registros (logs) del pedido lo llevan.
- Cada evento que genera el pedido lo lleva en `CorrelacionId` y en la propiedad `CorrelationId` del
  mensaje.
- El worker lo pone en sus registros al procesar el evento. Los avisos a los comercios lo tienen que
  mandar como encabezado `X-Correlation-Id`.

Con ese valor se busca en los registros de la API y del worker todo lo que pasó por un mismo pedido.

## Configuración

| Dónde | Qué |
|---|---|
| API | `ConnectionStrings:RabbitMQ` y `ConnectionStrings:Outbox` (usuario `lastmileuy_publicador`). Sin `RabbitMQ` no se publica: los eventos quedan en el outbox y salen cuando lo haya. |
| Worker | `ConnectionStrings:RabbitMQ`, `ConnectionStrings:DefaultConnection` (usuario `lastmileuy_app`) y, opcionales, `Mensajeria:EsperasReintento` y `Avisos:PermitirDestinosLocales` (solo desarrollo). |
| compose | Servicio `rabbitmq` (panel en http://localhost:15672). Contraseña: `RABBITMQ_PASSWORD`, con una de desarrollo por defecto. Servicio `lastmileuy.receptoravisos` (http://localhost:8085/avisos). |
