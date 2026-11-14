# Configuración del operador

Pantallas del Backoffice, en el menú **Configuración**, para que cada operador cargue sus propios datos de trabajo. Solo las usa el perfil **Operador**. El despachador no las ve y la API le responde 403.

| Pantalla | Qué se carga | Dónde se usa hoy |
| --- | --- | --- |
| Zonas de cobertura | Las zonas donde reparte la empresa y qué departamento y localidades cubre cada una | Al crear un envío, para saber su zona |
| Franjas horarias | Los horarios de entrega de cada zona | El comercio elige una al crear el envío. El armado de rutas y el orden sugerido las respetan |
| Cuadro tarifario | Precios por zona, modalidad, peso y volumen | Al crear un envío, para calcular su costo |
| Vehículos | Matrícula, tipo y capacidad de la flota | Al armar una hoja de ruta (solo se ofrecen los disponibles) |
| Reglas operativas | Intentos, plazos y evidencia por modalidad | Al registrar una entrega y al calcular la devolución |
| Motivos de no entrega | Las razones de una entrega fallida | La app del repartidor los descarga para elegir uno |

## Zona, franja y precio al crear un envío

Pasa igual por los tres caminos de alta: el portal, la API pública (`/api/v1/envios` y el lote) y `/api/envios`.

1. **Zona.** Sale de la dirección de entrega. Cada zona tiene un departamento y, opcionalmente, una lista de localidades. Una zona sin localidades cubre todo el departamento. La comparación con la ciudad de la dirección no distingue mayúsculas, tildes ni espacios de más. Si una zona nombra la localidad y otra cubre todo el departamento, gana la que la nombra. Una zona sin departamento no cubre nada; así quedan las que ya existían hasta que el operador las complete.
2. **Modalidad.** Estándar si no se manda; Urgente si se pide.
3. **Franja.** Es opcional. Si viene, tiene que ser una franja activa de la zona de la dirección; si no, la API responde 400 con el motivo. El portal solo ofrece las franjas de esa zona, que pide a `GET /api/envios/opciones-entrega?direccionId=`.
4. **Precio.** Se busca el cuadro que rige en ese momento y, dentro de él, una tarifa activa de la zona y la modalidad cuyo rango contenga el peso total y el volumen total de los bultos. Los rangos van de "más que el mínimo" a "hasta el máximo inclusive", así 0–5 y 5–10 no se pisan. Si dos tarifas sirven, se usa la de rango más angosto.
   - Costo = precio base + (peso total − peso mínimo de la tarifa) × precio por kg adicional.
   - Después se aplican los ajustes activos de la tarifa: los recargos suman su porcentaje y las bonificaciones lo restan. El costo nunca baja de 0 y se redondea a 2 decimales.
5. **Sin precio no se rechaza.** Si la dirección está fuera de cobertura, no hay cuadro vigente o ninguna tarifa coincide, el envío se crea igual, sin costo, y las pantallas muestran "Sin tarifa". Así un operador que todavía no cargó tarifas puede seguir recibiendo envíos.

El envío guarda la zona, la franja, el costo y el cuadro con el que se calculó. Si después cambia el cuadro, los envíos ya creados conservan su precio. El recargo por nueva visita de la entrega parcial se suma sobre ese costo.

**Dos zonas activas no pueden cubrir la misma localidad**, ni dos zonas pueden cubrir el mismo departamento entero; si pasa, la API responde 409. Una dirección no puede caer en dos zonas.

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
- `ZonaFranjaYTarifaEnAltaDePuntaAPuntaTests` (API): precio con el cuadro vigente, Urgente sin tarifa, peso fuera de rango, dirección fuera de cobertura, franja de otra zona o de otro operador, zonas superpuestas y alta por la API pública.
- `CalculoDeTarifaTests`: la fórmula del precio, los ajustes, el volumen y qué zona gana.
- `BackofficeConfiguracionTests` (navegador): vehículo con decimales, zona con franja, versión nueva del cuadro y acceso del despachador.
