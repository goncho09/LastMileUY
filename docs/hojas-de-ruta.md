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
La API no reordena, porque quien conoce el recorrido es el despachador (calles, tránsito,
acuerdos con los clientes). El campo `Parada.Orden` guarda ese número y la base no permite dos
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

## Validaciones al armar una ruta

| Regla | Respuesta |
|---|---|
| Nombre obligatorio (hasta 150 caracteres) y fecha de hoy en adelante (hora de Uruguay) | 400 |
| Entre 1 y 100 envíos, sin repetir ninguno | 400 |
| Repartidor y vehículo activos y del operador | 400 (uno de otro operador "no existe") |
| Envíos del operador, en depósito o reprogramados | 400 |
| Las franjas de los envíos tienen que estar activas y en orden (ver arriba) | 400 |
| El peso total de los bultos no supera la carga del vehículo; el volumen (largo × ancho × alto) tampoco | 400 |
| El repartidor no tiene otra ruta abierta ese día; el vehículo tampoco | 409 |
| Ningún envío está ya en otra ruta abierta | 409 |
| Otro usuario modificó un envío al mismo tiempo (concurrencia optimista) | 409 |

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
| `POST /api/rutas/{id}/despachar` | Operador, Despachador |
| `GET /api/rutas/mias` | Repartidor |
| `GET /api/rutas/mias/{id}` | Repartidor |
| `GET /api/rutas/{id}/hoja` | Repartidor |

## Datos de prueba (desarrollo)

`RutasSeeder` le da a cada operador de prueba dos vehículos (una moto de 30 kg / 0,15 m³ y una
furgoneta de 800 kg / 6 m³) y la ficha de repartidor de `repartidor@prueba.test` (operador 1) y
`repartidor2@prueba.test` (operador 2), que crea `IdentitySeeder` con la misma contraseña que el
resto de los usuarios de prueba.
