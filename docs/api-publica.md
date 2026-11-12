# API pública para comercios

Es la API para que el sistema de un comercio, como una tienda online o un ERP, cargue y siga sus envíos sin
pasar por el portal. Tiene documentación navegable, versión en la ruta, una clave por comercio y topes de
pedidos por clave.

Código:
- API: `src/LastMileUY.API/Modules/Integraciones`. Ahí están los endpoints de `/api/v1`, las claves, la
  autenticación, la importación masiva y el documento OpenAPI. También la migración `ClavesApi`.
- Portal: la pantalla **Integración** (`Components/Pages/Integracion.razor`).
- Pruebas:
  - `tests/LastMileUY.API.Tests/PuntaAPunta/ApiPublicaDePuntaAPuntaTests.cs`;
  - `tests/LastMileUY.E2E.Tests/PortalIntegracionTests.cs`, que la recorre en el navegador.

## Documentación navegable

| Qué | Dónde |
|---|---|
| Referencia interactiva (Scalar) | `/api/v1/docs/` |
| Documento OpenAPI | `/api/v1/openapi.json` |

- **Se publica en todos los ambientes, también en producción,** porque es lo que leen los integradores.
- **Solo trae los endpoints de `/api/v1`.**
- **Declara la clave como esquema de seguridad.** Así se pueden probar los pedidos desde la misma página, pegando una clave.
- **La documentación de toda la API** (portal, Backoffice, app del repartidor) sigue disponible solo en desarrollo, en `/scalar`.

En el portal, la pantalla Integración muestra el enlace a la documentación cuando se configura
`Api:UrlPublica`, que es la dirección de la API que ven los integradores. En Docker, la que usa el portal es interna.

## Claves

- **Quién las crea:** el comercio, desde el portal, en el menú *Integración*. Cada clave vale para su comercio y para el operador con el que inició sesión, igual que la sesión del portal. Si trabaja con dos operadores, crea una clave con cada uno.
- **Cuándo se ve:** la clave completa se muestra **una sola vez**, al crearla.
- **Qué se guarda:** la base guarda el hash SHA-256 de la clave, y además el comienzo (`lmuy_` y 8 caracteres), para reconocerla en la pantalla.
- **Por qué alcanza con SHA-256:** la clave tiene 256 bits al azar, así que no se puede adivinar. No hace falta un hash lento como el de las contraseñas.
- **Cómo se usa:** va en el encabezado `X-Api-Key`.
- **Revocación:** se revoca desde la misma pantalla y deja de funcionar en el pedido siguiente. No se borra: queda el registro, con la fecha de creación, el último uso y la revocación.
- **Tope:** hasta 10 claves activas por comercio y operador.

### Cómo se autentica un pedido

`AutenticacionClaveApi` es un esquema de autenticación aparte del de la cookie.

1. **Lee la clave:** toma `X-Api-Key`, calcula el hash y busca la clave sin revocar.
2. **Arma la identidad del comercio:** si la encuentra, arma la misma identidad que la sesión del portal después de elegir operador:
   - perfil `Comercio`;
   - `operador_id`;
   - `comercio_id`;
   - además, `clave_api_id`.
3. **Se aplica lo mismo que en el portal:** con esa identidad corren el filtro de EF, el RLS y la revisión de que el vínculo comercio–operador siga activo (`ValidarVinculoComercioMiddleware`). Si el operador deja de trabajar con el comercio, la clave responde `403`.

**Búsqueda bajo RLS:** al buscar la clave todavía no se sabe de qué operador es, y el RLS normal no dejaría ver la fila. La resuelve una política propia, `clave_api_por_hash`, que sigue el mismo patrón que el seguimiento público:
- deja ver solo la fila cuyo hash se cargó en `app.hash_clave_api`;
- la variable se carga dentro de una transacción, así que no queda en la conexión.

**Último uso:** se guarda como mucho una vez por minuto, para no escribir en la base en cada pedido.

### Alcance

- Los endpoints de `/api/v1` aceptan **solo** la clave. La cookie del portal no sirve ahí.
- El resto de la API acepta **solo** la cookie: con una clave, `/api/envios` responde `401`, y tampoco se pueden crear ni revocar claves.
- Sin clave, o con una inválida o revocada: `401`, con `WWW-Authenticate: ClaveApi header="X-Api-Key"`.

Los envíos creados con clave quedan con canal **Api**; los del portal, con canal Portal. El canal lo decide la API según cómo se autenticó el pedido, nunca por un dato del pedido.

## Versiones

Todo está bajo `/api/v1`.

- **Qué puede cambiar dentro de la v1:** solo lo compatible, como agregar campos a una respuesta o un endpoint nuevo.
- **Qué va a otra versión:** un cambio incompatible, como sacar o renombrar un campo, o cambiar una regla, saldría en `/api/v2`, con su propio documento OpenAPI, y la v1 seguiría funcionando.

**Cómo se cuida:** la forma de la v1 (rutas, parámetros y campos) está guardada en
`tests/LastMileUY.API.Tests/PuntaAPunta/ContratoApiPublicaV1.json`. La prueba
`El_contrato_de_la_v1_no_cambia_sin_querer` compara el documento OpenAPI con ese archivo.
La v1 devuelve los mismos objetos que usa el portal, así que, si alguien les cambia un campo para el portal, la prueba falla.
Entonces hay dos caminos:
- **Cambio compatible:** se actualiza el archivo a propósito, con `ACTUALIZAR_CONTRATO=1`.
- **Cambio incompatible:** va en una v2.

Los endpoints de `/api/v1` usan los mismos servicios que el portal, así que las validaciones y las reglas son las mismas. Lo que cambia es la forma del contrato:
- **El comercio no se manda:** sale de la clave.
- **Las rutas no cambian** aunque cambien las de la API interna.

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/v1/envios` | Lista paginada (`pagina`, `tamanioPagina` ≤ 100, `estado`, `buscar`) |
| GET | `/api/v1/envios/{id}` | Detalle: estado, bultos, historial e intentos |
| POST | `/api/v1/envios` | Crea un envío (`409` si la referencia ya existe) |
| POST | `/api/v1/envios/lote` | Importa hasta 100 envíos sin duplicar (ver abajo) |
| GET | `/api/v1/destinatarios` | Destinatarios con sus direcciones activas |
| POST | `/api/v1/destinatarios` | Alta de un destinatario con su primera dirección |
| POST | `/api/v1/destinatarios/{id}/direcciones` | Otra dirección de entrega |

## Importación masiva sin duplicados (req. 7)

`POST /api/v1/envios/lote` recibe hasta 100 envíos. La letra pide que reimportar un lote, completo o parcial, no
genere envíos duplicados. Para eso:

- **Referencia obligatoria:** cada envío tiene que traer su **referencia externa**, que es el número de pedido en el sistema del comercio.
- **Índice único:** la base tiene un índice único por operador + comercio + referencia.
- **Una fila por vez:** cada fila se procesa por separado y vuelve con su resultado:
  - `Creado`: se creó ahora;
  - `Existente`: ya había un envío con esa referencia. No se crea otro ni se modifica, y se devuelve el que estaba (id y código de seguimiento);
  - `Rechazado`: datos inválidos, con el motivo. No frena a las demás filas.
- **Repetidas en el mismo lote:** la segunda vuelve como `Existente`.
- **Dos importaciones a la vez:** si chocan con la misma referencia, el índice único frena a la segunda, y esa fila vuelve como `Existente`.

Así el comercio puede corregir las filas rechazadas y volver a mandar el lote entero. Las que ya entraron no se duplican.

```http
POST /api/v1/envios/lote
X-Api-Key: lmuy_...
Content-Type: application/json

{ "envios": [
  { "referenciaExterna": "PED-1045", "destinatarioId": 12, "direccionId": 30,
    "bultos": [ { "pesoKg": 1.5, "largoCm": 30, "anchoCm": 20, "altoCm": 10 } ] }
] }
```

```json
{ "creados": 1, "existentes": 0, "rechazados": 0,
  "resultados": [ { "indice": 0, "referenciaExterna": "PED-1045", "resultado": "Creado",
                    "envioId": 87, "codigoSeguimiento": "K7P2M9QX4T8B", "mensaje": null } ] }
```

## Límites de pedidos

El tope es **por clave**, no por IP. Varios comercios pueden salir por la misma IP, y un comercio con mucho
tráfico no frena a los demás. Además, está diferenciado por tipo de pedido:

| Política | Endpoints | Tope por minuto | Configuración |
|---|---|---|---|
| `ApiPublicaConsultas` | todos menos el lote | 120 | `ApiPublica:PedidosPorMinuto` |
| `ApiPublicaLotes` | `POST /api/v1/envios/lote` | 10 | `ApiPublica:LotesPorMinuto` |

Un lote puede crear hasta 100 envíos, así que pesa mucho más que una consulta: por eso tiene su propio tope.
Al pasarlo, la API responde `429` con el encabezado `Retry-After`, que indica los segundos a esperar, para que el sistema del comercio no reintente a ciegas.

Los topes viven en la memoria de cada instancia de la API: con varias instancias, cada una cuenta los suyos.

## Pendiente

- **Claves inválidas sin tope:** el tope de pedidos se aplica después de autenticar. Por eso, los pedidos con una clave inventada no se frenan, y cada uno cuesta una consulta a la base. Se podría sumar un tope por IP para los intentos fallidos, como el del seguimiento público.
- **Ambiente de pruebas:** son claves de prueba que trabajan sobre datos aislados de los reales, para que el comercio pruebe su integración sin crear envíos de verdad. Va en un cambio aparte.
