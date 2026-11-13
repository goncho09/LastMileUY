# Configuración del operador

Pantallas del Backoffice, en el menú **Configuración**, para que cada operador cargue sus propios datos de trabajo. Solo las usa el perfil **Operador**. El despachador no las ve y la API le responde 403.

| Pantalla | Qué se carga | Dónde se usa hoy |
| --- | --- | --- |
| Zonas de cobertura | Las zonas donde reparte la empresa | Catálogo: las franjas y las tarifas cuelgan de una zona |
| Franjas horarias | Los horarios de entrega de cada zona | Catálogo (ver la nota de abajo) |
| Cuadro tarifario | Precios por zona, modalidad, peso y volumen | Catálogo (ver la nota de abajo) |
| Vehículos | Matrícula, tipo y capacidad de la flota | Al armar una hoja de ruta (solo se ofrecen los disponibles) |
| Reglas operativas | Intentos, plazos y evidencia por modalidad | Al registrar una entrega y al calcular la devolución |
| Motivos de no entrega | Las razones de una entrega fallida | La app del repartidor los descarga para elegir uno |

**Todavía no se aplican a los envíos.** Al crear un envío no se le asigna zona, franja ni precio. Estas pantallas cargan los datos; el cálculo del precio y la elección de franja en el alta son el paso siguiente. El armado de rutas y el orden sugerido ya respetan la franja de un envío que la tenga.

## Reglas comunes

- **Nada se borra, se desactiva.** Los envíos, rutas e intentos viejos siguen apuntando a la zona, al vehículo o al motivo que usaron, y el historial no puede quedar roto. Lo inactivo deja de ofrecerse para lo nuevo.
- **Cada operador ve solo lo suyo.** Las tablas ya tenían el aislamiento por operador (filtro de EF y RLS). Si un operador pide el id de otro, recibe 404, igual que si no existiera.
- **Nombres repetidos dan 409.** Hay índices únicos por operador: zona y motivo por nombre, franja por nombre dentro de su zona y vehículo por matrícula. Si dos personas guardan a la vez, la base corta la segunda y se le muestra el motivo.
- **Datos inválidos dan 400 con el motivo**, por ejemplo una franja que termina antes de empezar o un peso mínimo mayor que el máximo.
- Tiene que quedar **al menos un motivo activo**, porque sin motivos el repartidor no puede registrar una entrega fallida.

## Reglas operativas: una vigente por modalidad

Hay como máximo una regla activa para Estándar y una para Urgente, y lo garantiza un índice único filtrado de la base. Crear o activar una regla desactiva la anterior de la misma modalidad. Las dos cosas se hacen en una transacción y en ese orden, para no chocar con el índice.

## Cuadro tarifario: versiones con fecha

Los precios no se editan en el lugar. Cada cambio es una **versión nueva** con su fecha de inicio:

- La versión nueva empieza a regir **desde mañana o más adelante**, y siempre después de la última. Ese día la anterior deja de regir.
- Mientras no empezó (**Programado**), se le pueden agregar, cambiar o desactivar tarifas. Puede arrancar con una copia de las tarifas de la versión anterior.
- Cuando ya rige (**Vigente**) o terminó (**Vencido**), queda de solo lectura y la API responde 409. Así, cuando se cobren los envíos, cada uno va a poder usar el cuadro que regía en su fecha.
- Las fechas se toman en hora de Uruguay: una versión empieza a las 00:00 de ese día.

## API

Todo está bajo `/api/operador` y pide el rol Operador.

| Método | Ruta |
| --- | --- |
| GET, POST | `/zonas` |
| GET, PUT | `/zonas/{id}` |
| PUT | `/zonas/{id}/activa` |
| POST | `/zonas/{zonaId}/franjas` |
| PUT | `/zonas/{zonaId}/franjas/{id}`, `/zonas/{zonaId}/franjas/{id}/activa` |
| GET, POST | `/vehiculos`, `/motivos-no-entrega`, `/reglas-operativas` |
| PUT | `/{recurso}/{id}`, `/{recurso}/{id}/activo` (`/activa` en reglas) |
| GET, POST | `/cuadros-tarifarios` |
| GET | `/cuadros-tarifarios/{id}` |
| POST | `/cuadros-tarifarios/{cuadroId}/tarifas` |
| PUT | `/cuadros-tarifarios/{cuadroId}/tarifas/{id}`, `.../{id}/activa` |

## Pruebas

- `ConfiguracionOperadorDePuntaAPuntaTests` (API): alta, repetidos, datos inválidos, aislamiento entre operadores, una regla vigente por modalidad, versiones del cuadro y permisos.
- `BackofficeConfiguracionTests` (navegador): vehículo con decimales, zona con franja, versión nueva del cuadro y acceso del despachador.
