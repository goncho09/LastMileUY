# API pública para comercios

Es la API para que el sistema de un comercio, como una tienda online o un ERP, cargue y siga sus envíos sin
pasar por el portal. Tiene documentación navegable, versión en la ruta, una clave por comercio, topes de
pedidos por clave y un ambiente de pruebas con datos aislados de los reales.

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
- **Qué se guarda:** la base guarda el hash SHA-256 de la clave, y además el comienzo (`lmuy_live_` o `lmuy_test_` y 6 caracteres), para reconocerla en la pantalla.
- **Por qué alcanza con SHA-256:** la clave tiene 256 bits al azar, así que no se puede adivinar. No hace falta un hash lento como el de las contraseñas.
- **Cómo se usa:** va en el encabezado `X-Api-Key`.
- **Revocación:** se revoca desde la misma pantalla y deja de funcionar en el pedido siguiente. No se borra: queda el registro, con la fecha de creación, el último uso y la revocación.
- **Tope:** hasta 10 claves activas por comercio y operador (`ApiPublica:ClavesActivas`).

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

Cada envío, suelto o en lote, acepta además dos campos opcionales:

- `modalidad`: `Estandar` (por defecto) o `Urgente`;
- `franjaHorariaId`: una franja activa de la zona de la dirección.

La zona y el costo los calcula la API a partir de la dirección y del cuadro tarifario vigente, y vuelven en la respuesta (`zona`, `franja`, `costoEnvio`). Si no hay tarifa que corresponda, el envío se crea con `costoEnvio: null`. El detalle está en [configuracion-operador.md](configuracion-operador.md).

```http
POST /api/v1/envios/lote
X-Api-Key: lmuy_live_...
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
tráfico no frena a los demás. Además, está diferenciado por tipo de pedido y por ambiente:

| Política | Endpoints | Producción, por minuto | Pruebas, por minuto | Configuración |
|---|---|---|---|---|
| `ApiPublicaConsultas` | todos menos el lote | 120 | 60 | `ApiPublica:PedidosPorMinuto`, `ApiPublica:Pruebas:PedidosPorMinuto` |
| `ApiPublicaLotes` | `POST /api/v1/envios/lote` | 10 | 5 | `ApiPublica:LotesPorMinuto`, `ApiPublica:Pruebas:LotesPorMinuto` |

- **Por qué el lote tiene su propio tope:** un lote puede crear hasta 100 envíos, así que pesa mucho más que una consulta.
- **Por qué las claves de prueba tienen la mitad:** sirven para probar la integración, no para cargar en volumen.
Al pasarlo, la API responde `429` con el encabezado `Retry-After`, que indica los segundos a esperar, para que el sistema del comercio no reintente a ciegas.

Los topes viven en la memoria de cada instancia de la API: con varias instancias, cada una cuenta los suyos.

## Ambiente de pruebas

Sirve para que el comercio pruebe su integración sin crear envíos de verdad.

- **Cómo se pide:** al crear la clave en el portal se elige el ambiente **Pruebas**. La clave empieza con `lmuy_test_`; las de producción, con `lmuy_live_`.
- **Qué endpoints usa:** los mismos, en la misma dirección. Lo que cambia son los datos con los que trabaja.

### Cómo se aíslan los datos

- **Envíos:** el ambiente de pruebas es **un operador más**, marcado con `EsAmbientePruebas`.
  - Con una clave de prueba, la identidad del pedido lleva ese operador en lugar del real. Por eso los envíos que crea son suyos, y quedan separados por la misma barrera que separa a dos operadores (filtro de EF + RLS, ADR-01).
  - No hubo que tocar ninguna consulta de envíos, rutas o sincronización: para el operador real, esos envíos no existen y nunca salen en una ruta.
  - La referencia externa puede repetirse entre los dos ambientes, porque el índice único incluye al operador.
- **Destinatarios:** son del comercio, no del operador, así que necesitan su propia marca, `EsPrueba`. Hay dos barreras:
  - **Filtro de EF:** con una clave de prueba solo se ven los de prueba, y en cualquier otro caso solo los de verdad. Eso incluye las claves de producción, el portal y el personal del operador.
  - **RLS:** la política `ambiente_destinatarios`, restrictiva, compara `EsPrueba` con la variable `app.ambiente_pruebas`. La variable la carga el interceptor de la conexión: `1` con una clave de prueba y vacía en cualquier otro caso. Sin la variable se ven los de verdad, así que ninguna conexión que no sepa del ambiente pierde datos.
  - **La marca no la decide el pedido:** la pone el contexto de EF al crear el destinatario, según quién lo crea, y no se puede cambiar después. La política también controla que coincida al insertar.
- **Las direcciones** siguen a su destinatario.

### El operador de pruebas

- **Cuándo se crea:** la primera vez que un comercio pide una clave de prueba. Hay uno solo, garantizado por un índice único parcial.
- **El vínculo:** al crear la clave se vincula con ese comercio, así el primer pedido ya funciona.
- **No aparece en ningún lado:**
  - ni para elegir en el login del portal;
  - ni en la administración de operadores, funcionalidades o vínculos;
  - el vínculo no se puede desactivar desde la administración.
- **Clave y operador son distintos:** la clave guarda el operador con el que se creó, para que el comercio la vea y la revoque en el portal junto a las demás. Pero trabaja con el operador de pruebas.
- **Varios comercios en pruebas:** entre ellos se separan por comercio, como en producción.

### Simular el recorrido

En pruebas no hay depósito, rutas ni repartidores, así que un envío quedaría siempre en Admitido.
`POST /api/v1/pruebas/envios/{id}/simular` con `{ "estado": "Entregado" }` lo hace avanzar por el recorrido
normal hasta el estado pedido. Los estados posibles son `EnDeposito`, `AsignadoARuta`, `EnTransito`, `Entregado` y `NoEntregado`.

- **Mismas reglas:** cada paso pasa por `Envio.CambiarEstado`, así que vale la misma tabla de transiciones (req. 11). De Entregado, por ejemplo, no se vuelve atrás.
- **Historial:** cada paso queda registrado con origen "Simulación".
- **Solo con clave de prueba:** la política `ApiPublicaPruebas` lo exige. El servicio lo vuelve a controlar, y además solo busca envíos del operador de pruebas. Con una clave de producción, la respuesta es `403` y el envío no cambia.

### Qué no cubre

- **Avisos HTTP:** las suscripciones se configuran en el portal con el operador elegido, y el operador de pruebas no se puede elegir.
- **Seguimiento público:** un código de prueba sí se puede consultar en la página pública. Aparece con el operador "Ambiente de pruebas (API pública)".

## Claves inválidas

El tope de pedidos va por clave y corre después de autenticar, así que una clave inventada nunca llega a él.
Sin otro freno, cada intento costaría una consulta a la base. Por eso `IntentosFallidosClaveApi` cuenta los fallos por IP:

- **Al pasar el tope:** pasados `ApiPublica:IntentosFallidosPorMinuto` fallos en un minuto (30 por defecto), esa IP recibe `429` con `Retry-After`, **sin consultar la base**, hasta que termina el minuto.
- **El costo:** mientras dura se frena todo pedido con clave desde esa IP, también uno con una clave buena. Es el precio de no consultar la base, y dura como mucho un minuto.
- **Adivinar una clave sigue siendo imposible** (256 bits). El freno es para que nadie use esto para cargar la base.
- **Instancias:** el contador vive en la memoria de cada instancia, como los demás topes.
- **Detrás del proxy de Azure** se cuenta por la IP real del visitante, no por la del proxy (ver "Detrás de un proxy inverso" en [seguridad.md](seguridad.md)). Si no, un solo abusador frenaría la API pública para todos los comercios.
