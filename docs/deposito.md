# Depósito

Pantallas del Backoffice, en el menú **Depósito**, para lo que pasa con los bultos dentro del depósito del operador. Las usan el **operario de depósito**, el operador y el despachador. El operario de depósito solo ve esta sección: al entrar al Backoffice va directo ahí y no ve envíos, rutas ni configuración.

## Recepción de bultos

Cuando el comercio crea un envío, queda **Admitido**: el sistema lo conoce pero los paquetes todavía no llegaron. La recepción es el momento en que el depósito los recibe físicamente y los compara con lo que declaró el comercio.

**Cómo se usa.** Se escanea o se escribe el código del envío o el de cualquiera de sus bultos (`CODIGO-1`, `CODIGO-2`…). La pantalla muestra todos los bultos del envío y, para cada uno, se indica:

| Cómo llegó | Qué significa |
|---|---|
| Recibido | Llegó bien. |
| Dañado | Llegó, pero con algún daño. |
| No llegó | El comercio lo declaró pero no está. |

Para cada bulto se puede anotar además el **peso medido**, que es opcional.

**Qué pasa con el envío:**

- **Todos los bultos llegaron** (recibidos o dañados): el envío pasa a **En depósito** y ya se puede asignar a una hoja de ruta.
- **Falta al menos un bulto:** el envío **sigue Admitido** y no se puede repartir. La recepción queda como discrepancia y el envío sigue en "Por recibir", marcado. Cuando llega lo que faltaba, se vuelve a recibir; esa recepción completa cierra sola la discrepancia anterior.

**Discrepancias.** Una recepción tiene discrepancia si algún bulto llegó dañado, no llegó o tiene un peso medido que difiere más de un 10 % del declarado. Para pesos chicos el margen mínimo es de 100 g. Las discrepancias aparecen en "Discrepancias pendientes" hasta que alguien las marca como resueltas y cuenta cómo, por ejemplo "se le avisó al comercio".

**Lo que no cambia:**

- El estado de los bultos (`Pendiente`, `Entregado`…) es el de la entrega y no se toca: la recepción se guarda aparte.
- El precio del envío tampoco cambia, aunque el peso medido sea distinto. Queda la discrepancia para que el operador lo hable con el comercio.

**Por qué así:**

- **Un envío incompleto no se reparte:** si saliera, el repartidor iría con menos bultos de los que espera el destinatario, y la entrega parcial está pensada para lo que pasa en la puerta, no en el depósito.
- **Tablas propias:** `RecepcionesDeposito` y `RecepcionesBulto` guardan quién recibió, cuándo y cómo llegó cada bulto, sin mezclarlo con el estado de entrega que usan la app del repartidor y la sincronización.

## Carga de vehículos

Antes de que una hoja de ruta salga, el depósito verifica que cada bulto esté arriba del vehículo. Está en la pestaña **Carga de vehículos** y también se llega desde el detalle de una ruta planificada.

**Cómo se usa.** Se escanea cada bulto a medida que sube. Cada código marca su casilla, y también se puede tildar a mano. Al guardar, la verificación queda registrada con quién la hizo y qué bultos estaban.

| Estado de la carga | Qué significa |
|---|---|
| Sin verificar | Nadie verificó esta ruta todavía. |
| Faltan bultos | La última verificación no encontró algún bulto. |
| Carga verificada | La última verificación encontró todos los bultos que la ruta tiene ahora. |

**Si falta un bulto:**

- se busca, se carga y se vuelve a verificar; o
- se **baja su envío de la ruta**. El envío vuelve a **En depósito**, queda disponible para otra hoja de ruta y las paradas que quedan se renumeran.

La ruta tiene que estar Planificada: una vez despachada, la carga ya no se cambia.

**Verificación obligatoria.** Con la funcionalidad `verificacion-carga` prendida para el operador, una ruta **solo se despacha con la carga verificada**; si no, la API responde 409 con el motivo. Viene apagada para no cambiar de golpe cómo trabajan los operadores que ya despachan; la prende el administrador desde Funcionalidades. Apagada, la verificación se puede hacer igual pero no frena el despacho.

**Por qué así:** la verificación se guarda aparte (`VerificacionesCarga` y `VerificacionesCargaBulto`) y la ruta no cambia de estado. Si hay varias verificaciones vale la última, así que verificar de nuevo después de cargar lo que faltaba alcanza.

## API

Todo está bajo `/api/deposito` y lo pueden usar los perfiles Operador, Despachador y Operario de depósito.

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/por-recibir` | Los envíos Admitidos, del más viejo al más nuevo. |
| GET | `/recepcion/{codigo}` | Busca por código de envío o de bulto. Devuelve los bultos y las recepciones anteriores. |
| POST | `/recepciones` | `{ envioId, bultos: [{ codigo, estado, pesoMedidoKg? }], observaciones? }`. Hay que informar cada bulto del envío una sola vez. 400 si falta o sobra alguno, 409 si el envío ya no está Admitido. |
| GET | `/discrepancias?pendientes=true` | Las recepciones con discrepancia; con `pendientes=false` también las resueltas. |
| POST | `/discrepancias/{id}/resolver` | `{ resolucion }`. 409 si no estaba pendiente. |
| GET | `/cargas` | Las rutas Planificadas con su cantidad de envíos y bultos y el estado de la carga. |
| GET | `/cargas/{rutaId}` | Los bultos de la ruta, por parada, y si estaban en la última verificación. |
| POST | `/cargas/{rutaId}/verificaciones` | `{ bultosCargados: [codigos], observaciones? }`. 400 si un código no es de la ruta, 409 si la ruta ya salió. |
| POST | `/cargas/{rutaId}/envios/{envioId}/quitar` | Baja el envío de la ruta y lo vuelve a EnDeposito. 409 si la ruta ya salió. |

## Aislamiento

`RecepcionesDeposito` tiene `OperadorId` y `ComercioId`, con filtro de EF y RLS igual que los envíos. `RecepcionesBulto` hereda el aislamiento de su recepción. `VerificacionesCarga` tiene `OperadorId` con filtro y RLS, y `VerificacionesCargaBulto` hereda de su verificación. Un operador no ve ni puede recibir envíos de otro: la API responde 404.

## Pruebas

- `RecepcionDepositoDePuntaAPuntaTests` (API por HTTP con PostgreSQL):
  - recepción incompleta y después completa;
  - peso distinto y bulto dañado;
  - resolución de discrepancias;
  - validaciones;
  - aislamiento entre operadores, también contado directo en la base con el usuario de la aplicación;
  - un operario de depósito real recibe pero no puede planificar rutas.
- `CargaVehiculoDePuntaAPuntaTests` (API): verificación incompleta y completa, códigos ajenos, despacho bloqueado con la funcionalidad prendida, bajar un envío (vuelve a En depósito y se renumeran las paradas), y que otro operador no ve la carga.
- `PermisosTests` (Backoffice): qué ve el operario de depósito.
