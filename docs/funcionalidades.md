# Funcionalidades por operador (banderas)

El administrador de la plataforma prende o apaga partes del sistema **para cada operador**, sin desplegar de
nuevo. Sirve para dar una funcionalidad a un operador antes que a los demás, para apagar una que le trae
problemas a uno solo, o para que un operador no use algo que no contrató.

## Cuáles hay

| Clave | Nombre | Por defecto | Qué pasa si está apagada |
|---|---|---|---|
| `orden-sugerido` | Orden sugerido de paradas | Prendida | El Backoffice no muestra "Ordenar por recorrido" y `POST /api/rutas/sugerir-orden` contesta **403** con el motivo. |
| `seguimiento-publico` | Seguimiento público | Prendida | `GET /api/seguimiento/{codigo}` contesta **404** para los envíos de ese operador, como si el código no existiera. |
| `avisos-comercios` | Avisos a comercios | Prendida | El comercio ve sus suscripciones, pero crearlas, cambiarlas o regenerar la clave da **403** con el motivo (el portal lo muestra). El worker deja de registrar avisos nuevos para ese operador. Los que ya estaban pendientes se mandan igual: se generaron cuando estaba prendida. |

El catálogo está en `LastMileUY.Contracts/Operadores/Funcionalidades.cs`: clave, nombre, descripción y
valor por defecto. Agregar una nueva es sumarla ahí y poner el corte donde corresponda.

## Dónde se guardan

- Tabla `FuncionalidadesOperador`: una fila por operador y funcionalidad, **solo si el administrador decidió
  algo**. Sin fila vale el valor por defecto, así que una funcionalidad nueva queda con su valor por defecto
  para todos los operadores sin migrar datos.
- Guarda quién la cambió por última vez (`ModificadaPor`) y cuándo.
- **Sin RLS, a propósito**, igual que `Operadores`: la leen el seguimiento público y el administrador, que no
  tienen operador. Las protecciones son otras:
  - solo el perfil Administrador la cambia (`/api/admin`);
  - `FuncionalidadesService` siempre filtra por el operador que se pide;
  - la prueba `Toda_tabla_tiene_RLS_forzado_salvo_las_justificadas` la tiene en su lista, con el motivo.

## API

| Método | Ruta | Quién | Qué hace |
|---|---|---|---|
| GET | `/api/admin/funcionalidades` | Administrador | Cada operador con todas sus funcionalidades (`Activa`, `PorDefecto`). |
| PUT | `/api/admin/operadores/{operadorId}/funcionalidades/{clave}` | Administrador | `{ "activa": false }`. 204, 404 si el operador no existe, 400 si la funcionalidad no existe. |
| GET | `/api/operador` | Cualquier usuario con operador | Además de lo de siempre, `funcionalidades`: las claves prendidas para su operador. |

`GET /api/operador` está en el caché (Redis). Cambiar una bandera lo invalida (`InvalidacionCacheInterceptor`),
así que el cambio se ve enseguida.

**Seguimiento público y caché:** las respuestas del seguimiento quedan guardadas hasta 2 minutos. Un código
que se consultó justo antes de apagar el seguimiento puede seguir viéndose hasta que venza. Es el mismo margen
que ya tiene un cambio de marca del operador.

## Backoffice

- **Funcionalidades** (menú del administrador): cada operador con sus funcionalidades, prendidas o
  apagadas, y un botón para cambiar cada una.
- **Nueva hoja de ruta**: sin `orden-sugerido`, no aparece "Ordenar por recorrido". Si la API igual lo
  rechaza, por ejemplo si se apagó con la pantalla abierta, se muestra su motivo.

## Cómo se prueba

- `FuncionalidadesDePuntaAPuntaTests` (API por HTTP con los usuarios reales):
  - solo el administrador ve y cambia las banderas;
  - una clave o un operador que no existen se rechazan;
  - con cada funcionalidad apagada, su corte funciona para ese operador y no para el otro, y vuelve a andar al prenderla.
- `ProcesadorAvisosTests.Con_los_avisos_apagados_para_el_operador_no_registra_avisos` (worker contra la base real).
- `FuncionalidadesTests` (Backoffice):
  - la pantalla del administrador;
  - el botón oculto;
  - el motivo del rechazo.
- Mutación: sin el corte del orden sugerido o el del seguimiento, sus pruebas fallan.
