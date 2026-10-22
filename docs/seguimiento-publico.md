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
| Nombre, color y logo del operador que entrega | El Id del envío, el comercio, los bultos, el costo, la firma y la foto |

Del historial solo van los eventos marcados como públicos (`EventoEnvio.EsPublico`).

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
