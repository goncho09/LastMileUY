# Seguimiento público de envíos

El destinatario consulta cómo va su envío con el código de seguimiento, sin cuenta ni inicio de
sesión. Es la única parte del sistema que muestra un envío a alguien que no inició sesión, así que este
documento dice qué se muestra y cómo se cuida el aislamiento entre operadores (ADR-01).

Código:
- API: `GET /api/seguimiento/{codigo}` (`src/LastMileUY.API/Modules/Seguimiento`) y la migración
  `SeguimientoPublico`.
- Página: `src/LastMileUY.PublicTracking` (en Docker, puerto 8084). Le consulta a la API desde el
  servidor; el navegador nunca habla con la API.

Pruebas: `tests/LastMileUY.API.Tests/Multitenancy/SeguimientoPublicoTests.cs` (la política de la base)
y `tests/LastMileUY.API.Tests/PuntaAPunta/SeguimientoPublicoDePuntaAPuntaTests.cs` (por HTTP).

## Qué se muestra

| Se muestra | No se muestra |
|---|---|
| Estado actual y el historial de estados, con la hora | El destinatario (nombre, teléfono, email) |
| Ciudad y departamento de destino | La calle, el número y la referencia |
| Fecha de despacho y de entrega | Las observaciones de cada cambio de estado (pueden ser notas internas) |
| Nombre, color y logo del operador que entrega | El Id del envío, el comercio, el costo, la firma y la foto |
| Avance de la entrega por bultos: cuántos se entregaron de cuántos, y cuántos volvieron al comercio | Los códigos e ids de los bultos, y los motivos de los que no se entregaron |

Del historial solo van los eventos marcados como públicos (`EventoEnvio.EsPublico`).

**Avance por bultos (entrega parcial).** El seguimiento cuenta los bultos por estado (`AvanceBultosDto`:
total, entregados, devueltos) y la página muestra, por ejemplo, "2 de 3 bultos entregados" con una barra,
solo si el envío tiene más de un bulto. Los bultos se leen con la misma política RLS que los eventos: se
ven solo los del envío del código cargado. Como el seguimiento está en el caché, `InvalidacionCacheInterceptor`
lo borra también cuando cambia un bulto, aunque no cambie el envío.

## Cómo se consulta sin operador

Sin sesión no hay operador, y las dos barreras del ADR-01 no dejan ver ningún envío: el filtro de EF
pide un operador y la política RLS `aislamiento_operador` compara contra `app.operador_id`, que está
vacío. En vez de saltear RLS (con un usuario con más permisos), se agregó una política más:

```sql
CREATE POLICY seguimiento_publico ON "Envios"
    FOR SELECT
    USING ("CodigoSeguimiento" = NULLIF(current_setting('app.codigo_seguimiento', true), ''));
```

- Las políticas de una tabla se suman con OR: un envío se ve si es del operador cargado **o** si su
  código es el cargado en `app.codigo_seguimiento`.
- Es solo `FOR SELECT`: con el código se puede leer el envío, nunca modificarlo ni borrarlo.
- `SeguimientoPublicoService` carga el código con `set_config(..., true)` dentro de una transacción. El
  `true` hace que valga solo en esa transacción: cuando termina, la conexión vuelve al pool sin el
  código. Además, el interceptor de conexión lo vacía cada vez que se abre una (como hace con el
  operador y el comercio).
- El servicio usa `IgnoreQueryFilters` (el filtro de EF pide operador y acá no hay); la barrera es la
  política, que solo devuelve el envío de ese código. Lo comprueba la prueba
  `La_base_solo_deja_ver_el_envio_del_codigo_cargado`, que pide todos los envíos y recibe solo uno.

## Por qué no se pueden adivinar códigos

- El código tiene 12 caracteres elegidos al azar entre 31 (`Envio.GenerarCodigoSeguimiento`, con
  `RandomNumberGenerator`): son unos 8 × 10¹⁷ códigos posibles. Probar códigos al azar no encuentra nada.
- El Id del envío (1, 2, 3...) nunca se expone, porque ese sí sería fácil de recorrer.
- Un código que no existe y uno mal escrito dan la misma respuesta (404), sin pistas.
- Tope de consultas por IP: la página permite 20 por minuto a cada visitante y la API 120 por minuto
  a cada IP (todas las consultas de la página llegan desde la misma IP, la de su servidor). Al pasarlo
  responden 429. Se configuran con `Seguimiento:ConsultasPorMinuto` en cada proyecto.

## Operadores sin seguimiento público

El administrador puede apagar el seguimiento público para un operador (ver [funcionalidades.md](funcionalidades.md)). Si está apagado, los códigos de ese operador contestan 404, igual que un código que no existe.

## Reprogramación pedida por el destinatario (req. 26)

En la página de seguimiento, si el envío lo permite, aparece "¿No vas a estar?": el destinatario elige otro día (y, si su zona tiene franjas, una franja) y deja un comentario opcional para el repartidor.

**Las reglas las pone el operador** (`PoliticaReprogramacion`), con la regla operativa con la que se creó el envío:

| Situación | ¿Se puede? |
|---|---|
| Admitido, en depósito, no entregado o reprogramado | Sí |
| Asignado a ruta o en tránsito | No: "ya está en camino" |
| Entregado, en devolución, devuelto o extraviado | No |
| Intentos fallidos ≥ máximo de la regla | No: el envío vuelve al comercio |
| Ya pidió una reprogramación desde el último intento | No: una por intento |

- **Fecha mínima:** hoy (hora de Uruguay) + las horas entre intentos de la regla, redondeadas a días, al menos 1. **Fecha máxima:** 14 días.
- **Franja:** solo las activas de la zona del envío. Se manda por nombre, no por id, para no exponer identificadores internos.

**Qué pasa al pedirla** (`POST /api/seguimiento/{codigo}/reprogramacion`, anónimo y con la misma limitación de tasa):

- Se guarda la solicitud en `SolicitudesReprogramacion` (con `OperadorId`, filtro de EF y RLS).
- El envío guarda la **fecha pedida** y, si eligió, la nueva franja.
- Si estaba **No entregado**, pasa a **Reprogramado** por la máquina de estados, con el pedido en el historial. Eso dispara el correo "Reprogramamos la entrega de tu envío".
- **Al planificar:** no se puede asignar el envío a una ruta con fecha anterior a la pedida (400 con el motivo).
- Se invalida el caché del seguimiento de ese código.
- Respuestas: 204 si se aceptó, 409 con el motivo si las reglas no lo permiten, 404 si el código no existe o el operador no tiene seguimiento público.

**Cómo escribe sin usuario:** la página es anónima, así que no hay operador en la sesión. La API ubica el envío por el código (como la consulta) y después abre un contexto de base **solo de ese operador** (`FabricaContextoOperador`), con los mismos filtros, RLS, outbox e invalidación de caché que una petición normal. El destinatario no puede tocar nada más que su envío.

**Pruebas:**
- `ReprogramacionPublicaDePuntaAPuntaTests`:
  - asignado no se puede;
  - tras un intento fallido se puede;
  - una fecha fuera de rango o una franja inventada dan 409;
  - el pedido válido lo deja Reprogramado con la fecha;
  - un segundo pedido da 409;
  - no se puede planificar antes de la fecha pedida, el mismo día sí;
  - un código inexistente da 404.
- `PoliticaReprogramacionTests` cubre cada fila de la tabla y la fecha mínima.
