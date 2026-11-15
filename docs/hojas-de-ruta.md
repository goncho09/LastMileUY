# Hojas de ruta

Una hoja de ruta es el recorrido de un repartidor en un día: un vehículo y una lista ordenada de
paradas, una por envío. La arma y la despacha el personal del operador (perfiles **Operador** y
**Despachador**) y la descarga el **Repartidor** asignado.

Código: `src/LastMileUY.API/Modules/Rutas` (`PlanificarRutaService`, `ConsultasRutasService`,
`RutasEndpoints`). Pruebas: `tests/LastMileUY.API.Tests/Rutas` y
`tests/LastMileUY.API.Tests/PuntaAPunta/HojasDeRutaDePuntaAPuntaTests.cs`.

## Estados

| Estado | Qué significa | Envíos |
|---|---|---|
| Planificada | Recién armada. Se puede revisar antes de que salga. | AsignadoARuta |
| Despachada | Salió del depósito. El repartidor ya puede descargarla. | EnTransito |
| EnCurso / Finalizada / Cancelada | Los maneja la app del repartidor y la sincronización (siguiente etapa). | — |

Planificada, Despachada y EnCurso son estados **abiertos**: mientras la ruta está en uno de ellos,
su repartidor, su vehículo y sus envíos están ocupados.

## Orden de las paradas

**El orden lo decide quien arma la ruta.** Los envíos se mandan en una lista (`EnvioIds`) y el
orden de la lista es el orden de visita: el primero es la parada 1, el segundo la parada 2, etc.
Al armar la ruta la API no reordena, porque quien conoce el recorrido es el despachador (calles,
tránsito, acuerdos con los clientes). Para ayudarlo, puede pedir un **orden sugerido** antes de
armarla (ver abajo): es una sugerencia que él revisa y cambia si quiere. El campo `Parada.Orden` guarda ese número y la base no permite dos
paradas con el mismo orden en una ruta (índice único `RutaId + Orden`).

La API solo controla una regla del orden, la de las **franjas horarias**:

- Las paradas cuyo envío tiene franja tienen que ir en orden de horario (por hora de inicio).
  No se puede visitar una franja de la mañana después de una de la tarde.
- Las paradas sin franja pueden ir en cualquier lugar de la lista.

Ejemplo, con las franjas Mañana (9 a 12) y Tarde (14 a 18):

| Lista enviada | Resultado |
|---|---|
| Mañana, sin franja, Tarde | Se acepta |
| sin franja, Mañana, Mañana, Tarde | Se acepta |
| Tarde, sin franja, Mañana | Se rechaza (la parada 3 no puede ir después de la parada 1) |

## Orden sugerido

En el Backoffice, "Nueva hoja de ruta" tiene el botón **Ordenar por recorrido**. Manda los envíos
tildados (en el orden en que están) a `POST /api/rutas/sugerir-orden`, y la respuesta trae el orden
sugerido y cuántos kilómetros tiene el recorrido antes y después. El Backoffice carga esos números de
orden en la pantalla. **No se guarda nada**: el despachador revisa los números, los cambia si quiere y
recién ahí arma la ruta como siempre.

**De dónde salen las ubicaciones.**
- Cada dirección de destinatario puede tener latitud y longitud (`Direccion.Latitud`/`Longitud`,
  opcionales). Las carga quien da de alta la dirección: el comercio en su portal (página
  Destinatarios, pegando las coordenadas como las copia Google Maps) o por la API
  (`POST /api/destinatarios` y `POST /api/destinatarios/{id}/direcciones`).
- Al crear un envío se copian junto con el resto de la dirección (`DireccionEntrega`), igual que la
  calle. Si después cambia la dirección del destinatario, el envío conserva la suya.
- El operador carga su **depósito** en "Mi operador" (`PUT /api/operador/deposito`, solo el perfil
  Operador). Es el punto de partida del recorrido. Sin depósito, los kilómetros se cuentan desde la
  primera parada.
- La base exige que cada ubicación esté completa (latitud y longitud juntas) y dentro del mapa:
  restricciones `CK_Direcciones_Ubicacion`, `CK_Envios_Ubicacion` y `CK_Operadores_Deposito`.
- No se usa ningún servicio externo para convertir direcciones en coordenadas. Las direcciones de los
  destinatarios no salen del sistema.

**Cómo se calcula** (`OrdenDeParadas`, en Contracts, así el Backoffice con datos de prueba usa el
mismo cálculo):

1. **Vecino más cercano:** desde el depósito, siempre a la parada más cercana que se pueda visitar.
2. **Mejoras locales**, mientras alguna acorte el recorrido:
   - "2-opt": dar vuelta un tramo, lo que saca los cruces;
   - "or-opt": sacar de 1 a 3 paradas seguidas y ponerlas en otro lugar.
3. **Varios arranques:** se repite empezando por las 8 paradas más cercanas al depósito, y también
   mejorando el orden que mandó el despachador. Se queda el más corto, así que nunca queda peor que lo
   que ya tenía.

Reglas que siempre cumple:

| Regla | Cómo |
|---|---|
| Franjas horarias | Las paradas con franja van en orden de horario (la misma regla que controla la API al armar la ruta). Una parada sin franja puede ir en cualquier lugar, también entre la mañana y la tarde. |
| Sin ubicación | No se pueden ubicar en el recorrido. Van al final o, si tienen franja, justo antes de la primera parada de una franja posterior. La respuesta las nombra (`SinUbicacion`) y la pantalla las marca "sin ubicación". |
| Distancia | En línea recta (fórmula del haversine), desde el depósito hasta la última parada, sin la vuelta. No es la distancia por calle, pero sirve para comparar un orden con otro. |

Qué tan bueno es: con 7 paradas se puede probar todos los órdenes posibles. En 150 casos al azar, el
sugerido quedó como mucho un 2 % más largo que el mejor posible; la prueba exige menos del 5 %. Con el
máximo de una ruta (100 paradas) tarda unos 0,2 segundos. Arrancando una sola vez, aun con las dos
mejoras, el peor caso era un 16 % más largo: el recorrido arrancaba para el lado equivocado y las mejoras locales no lo pueden
dar vuelta.

Pruebas: `tests/LastMileUY.API.Tests/Rutas/OrdenDeParadasTests.cs` (el cálculo),
`tests/LastMileUY.API.Tests/PuntaAPunta/OrdenSugeridoDePuntaAPuntaTests.cs` (por HTTP) y
`tests/LastMileUY.Backoffice.Tests/OrdenSugeridoTests.cs` (la pantalla y el depósito).

## Validaciones al armar una ruta

| Regla | Respuesta |
|---|---|
| Nombre obligatorio (hasta 150 caracteres) y fecha de hoy en adelante (hora de Uruguay) | 400 |
| Entre 1 y 100 envíos, sin repetir ninguno | 400 |
| Repartidor y vehículo activos y del operador | 400 (uno de otro operador "no existe") |
| Envíos del operador, en depósito o reprogramados | 400 |
| Las franjas de los envíos tienen que estar activas y en orden (ver arriba) | 400 |
| Cada envío tiene al menos un bulto sin entregar | 400 |
| El peso total de los bultos que salen no supera la carga del vehículo; el volumen (largo × ancho × alto) tampoco | 400 |
| El repartidor no tiene otra ruta abierta ese día; el vehículo tampoco. Lo asegura la base (índices únicos parciales sobre las rutas abiertas), también si dos despachadores arman rutas a la vez | 409 |
| Ningún envío está ya en otra ruta abierta | 409 |
| Otro usuario modificó un envío al mismo tiempo (concurrencia optimista) | 409 |

### Qué bultos lleva cada parada

Un envío que tuvo una entrega parcial y se reprogramó vuelve a salir **solo con los bultos que faltan**.
Al armar la ruta, cada parada guarda sus bultos en `BultosParada`: los Pendientes y los No entregados.
Ese es el manifiesto de la hoja de ruta:

- La capacidad del vehículo, la hoja de ruta (cantidad, peso y volumen de cada parada) y la lista de
  envíos para armar rutas cuentan esos bultos, no todo el envío.
- La lista de envíos para armar rutas (`GET /api/rutas/opciones`) solo trae los que tienen al menos un
  bulto por entregar. Un envío se crea con al menos un bulto (`POST /api/envios` sin bultos es un 400),
  pero si alguno quedó sin bultos de antes de exigirlos, no se ofrece porque no se puede asignar.
- Queda fijo: una ruta terminada sigue mostrando lo que llevó, aunque después esos bultos se entreguen
  en otra.
- La base no deja que una parada lleve un bulto de otro envío: la política RLS de `BultosParada` lo
  exige además del aislamiento entre operadores.
- La hoja que descarga el repartidor trae la lista de esos bultos (código, estado, descripción y peso),
  siempre en el mismo orden, para identificarlos con el escáner.
- Las rutas armadas antes del manifiesto no lo tienen; para esas se cuentan y se listan todos los
  bultos del envío.

## Despacho y descarga

- `POST /api/rutas/{id}/despachar`: solo una ruta Planificada. Todos sus envíos tienen que
  seguir en AsignadoARuta (si alguien devolvió uno al depósito a mano, se rechaza con 409). Los
  envíos pasan a EnTransito y queda en su historial "Salió en la ruta ...".
- `GET /api/rutas/mias`: las rutas despachadas o en curso del repartidor que inició sesión.
- `GET /api/rutas/{id}/hoja`: la hoja completa (paradas en orden, destinatario, dirección, franja,
  bultos). Responde 403 si la ruta es de otro repartidor, 409 si todavía no se despachó y 404 si es
  de otro operador (por el aislamiento, ni siquiera existe). Se guarda un minuto en el caché, pero los
  controles se hacen siempre contra la base, y cualquier cambio en la ruta o en sus envíos la borra
  del caché (`InvalidacionCacheInterceptor`).
- `GET /api/rutas/mias/{id}`: la misma hoja, para la pantalla de la app. Además de las despachadas y
  en curso, deja ver las finalizadas (para mostrar cómo terminó); nunca una planificada ni una de otro
  repartidor (404).
- Cada parada trae la `Modalidad` del envío y la `Evidencia` que pide la regla operativa vigente para
  esa modalidad (`Firma`, `Foto`, `DocumentoReceptor`). Así la app sabe qué pedir antes de entregar;
  la sincronización lo vuelve a controlar igual.

## Endpoints

| Método y ruta | Perfiles |
|---|---|
| `GET /api/rutas?fecha=&estado=&pagina=&tamanioPagina=` | Operador, Despachador |
| `GET /api/rutas/opciones` (repartidores, vehículos y envíos disponibles) | Operador, Despachador |
| `GET /api/rutas/{id}` | Operador, Despachador |
| `POST /api/rutas` | Operador, Despachador |
| `POST /api/rutas/sugerir-orden` (orden sugerido, no guarda nada) | Operador, Despachador |
| `PUT /api/operador/deposito` (de dónde salen las rutas) | Operador |
| `POST /api/rutas/{id}/despachar` | Operador, Despachador |
| `GET /api/rutas/mias` | Repartidor |
| `GET /api/rutas/mias/{id}` | Repartidor |
| `GET /api/rutas/{id}/hoja` | Repartidor |

## Datos de prueba (desarrollo)

`RutasSeeder` le da a cada operador de prueba dos vehículos (una moto de 30 kg / 0,15 m³ y una
furgoneta de 800 kg / 6 m³) y la ficha de repartidor de `repartidor@prueba.test` (operador 1) y
`repartidor2@prueba.test` (operador 2), que crea `IdentitySeeder` con la misma contraseña que el
resto de los usuarios de prueba.

Para el orden sugerido, `IdentitySeeder` le pone un depósito a cada operador de prueba (Paso Molino y
Camino Carrasco) y ubicación aproximada a las direcciones de prueba. Agrega además una destinataria
con cuatro direcciones repartidas por Montevideo. En una base cargada antes, completa la ubicación de
las direcciones de prueba que no la tenían.

## Posición de la flota y envíos en riesgo (req. 23 y 29)

**En la app del repartidor:** mientras la hoja de ruta está Despachada o En curso y la tiene abierta, la app toma la posición del celular cada 30 segundos (`posicion.js` con `watchPosition`). Cada posición se guarda primero en el celular y se manda en lotes cada minuto, o cuando vuelve la conexión: sin señal no se pierden, se mandan después con su hora real. Se guardan como máximo las últimas 500.

- **Límite de la PWA:** una aplicación web no puede seguir la posición con la pantalla apagada o en segundo plano, como pide la 6.11 para MAUI. El equipo es de 3 integrantes y la letra permite reemplazar la app MAUI por una PWA, así que el seguimiento funciona mientras la app está abierta.

**API:**
- `POST /api/rutas/mias/{id}/posiciones` (Repartidor): `{ posiciones: [{ latitud, longitud, fechaHora }] }`, hasta 200 por lote.
  - Solo sobre sus rutas (404 si no es suya) y en la calle (409 si no salió o ya terminó).
  - Descarta coordenadas inválidas, horas en el futuro o de hace más de 2 días, y repetidas: reenviar el mismo lote no duplica nada, porque la hora se guarda al segundo y no se repite por ruta.
  - Devuelve cuántas guardó y cuántas descartó.
- `GET /api/flota` (Operador, Despachador): las rutas en la calle con su última posición y cuántas paradas les quedan, y los **envíos en riesgo**.

**Envío en riesgo:** asignado o en tránsito, y con su límite vencido o a menos de una hora. El límite es el más cercano de:
- el fin de su franja horaria el día de la ruta, en hora de Uruguay;
- su plazo comprometido: fecha de alta + `PlazoEntregaHoras` de la regla con la que se creó.

**Backoffice:** página **Flota en vivo** (`/Rutas/Flota`), con mapa (Leaflet + OpenStreetMap), la tabla de envíos en riesgo y la de vehículos. Se actualiza cada 20 segundos y cada vez que llega por SignalR un cambio de estado de un envío. Una posición de hace más de 15 minutos se marca como desactualizada.

**Aislamiento:** `Posiciones` hereda de su ruta (filtro de EF y RLS por `EXISTS`), así que otro operador no ve la flota.

**Pruebas:**
- `FlotaDePuntaAPuntaTests`:
  - antes de despachar da 409;
  - un lote con repetidas, una latitud inválida y una hora futura guarda 2 y descarta 3;
  - reenviarlo no guarda nada;
  - el tablero muestra la última posición y las paradas pendientes;
  - otro repartidor recibe 404, otro operador no la ve, y un repartidor no entra al tablero.
- `LimiteEntregaTests`: el cierre de la franja en hora de Uruguay y que gana el límite más cercano.
