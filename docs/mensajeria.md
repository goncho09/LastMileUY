# Mensajería: eventos de envío con RabbitMQ

Cada vez que un envío entra al sistema o cambia de estado, la API publica un evento en RabbitMQ. El
worker (`LastMileUY.Worker`) lo consume y, a partir de él, se hacen los avisos a los comercios. Este
documento dice qué lleva cada evento, cómo se garantiza que no se pierda y cómo se sigue un cambio de
punta a punta.

Código:
- Contrato: `src/LastMileUY.Contracts/Mensajeria` (lo comparten la API y el worker).
- API: `src/LastMileUY.API/Infrastructure/Mensajeria` (outbox y publicador),
  `Infrastructure/Correlacion` y la migración `Outbox`.
- Worker: `src/LastMileUY.Worker/ConsumidorEventosEnvio.cs`.

Pruebas: `tests/LastMileUY.API.Tests/Mensajeria`.

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
tiene que descartar los repetidos por `EventoId`.

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
| Worker | `ConnectionStrings:RabbitMQ`. |
| compose | Servicio `rabbitmq` (panel en http://localhost:15672). Contraseña: `RABBITMQ_PASSWORD`, con una de desarrollo por defecto. |
