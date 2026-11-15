# Reportes de gestión y liquidación (req. 30)

**Backoffice → Reportes** (Operador y Despachador) muestra, para un período (por defecto los últimos 30 días, como máximo un año):

| Bloque | Qué cuenta |
|---|---|
| Volumen por día | Envíos dados de alta cada día (hora de Uruguay) y cuántos de ellos ya se entregaron. |
| Cumplimiento por zona y por comercio | Envíos, entregados, **entregados a tiempo** y no entregados (no entregado, en devolución, devuelto o extraviado). |
| Efectividad por repartidor | Intentos de entrega del período y cuántos fueron exitosos. |
| Motivos de no entrega | Intentos fallidos agrupados por motivo del catálogo del operador. |
| Liquidación por comercio | Envíos, entregados, devueltos, envíos sin tarifa e importe. Se descarga en CSV. |

- **A tiempo:** entregado antes de la fecha de alta + `PlazoEntregaHoras` de la regla con la que se creó el envío (gracias al versionado de reglas, cambiar el plazo no reescribe el pasado). Sin plazo, cualquier entrega cuenta como a tiempo.
- **Importe:** la suma del `CostoEnvio` que se calculó y guardó al dar de alta cada envío con el cuadro tarifario vigente. Los envíos que se crearon sin tarifa se cuentan aparte para que el operador los revise.
- **El período** se toma por fecha de alta del envío, en hora de Uruguay; los intentos y motivos, por la fecha del intento.

**Portal → Mi cuenta:** el comercio ve su cuenta corriente con el operador para el período (por defecto el mes en curso): importe, envíos, entregados, porcentaje a tiempo y el detalle por día. Usa el mismo cálculo filtrado a su comercio.

## API

| Método | Ruta | Perfil |
|---|---|---|
| GET | `/api/reportes?desde=&hasta=` | Operador, Despachador |
| GET | `/api/reportes/liquidacion.csv?desde=&hasta=` | Operador, Despachador |
| GET | `/api/reportes/mi-cuenta?desde=&hasta=` | Comercio (solo su comercio) |

400 si el período está al revés o supera un año. Todo pasa por los filtros por operador y RLS, así que cada operador solo reporta lo suyo.

## Pruebas

- `ReportesTests`: qué es "a tiempo", agrupación por zona con sus porcentajes y la suma de la liquidación con envíos sin tarifa.
- `ReportesDePuntaAPuntaTests`: crea una ruta, entrega un envío y falla otro, y verifica que el reporte suma 2 envíos, 1 entregado, 2 intentos y 1 motivo; que volumen y liquidación cuadran con el total; que el comercio ve solo su cuenta; el CSV; el período inválido; y que un comercio no entra al reporte del operador.

## Cierre diario e indicadores (worker)

El worker tiene un proceso programado, `CierreDiario`, que cada 15 minutos (`CierreDiario:MinutosEntreRecalculos`) recalcula los indicadores de cada operador activo:

- **El día anterior** (hora de Uruguay) queda **cerrado**: envíos dados de alta, entregados, entregados a tiempo, no entregados, intentos fallidos e importe.
- **El día de hoy** se recalcula en cada vuelta y queda **en curso**.
- Se guardan en `IndicadoresDiarios`, una fila por operador y día, con `OperadorId`, filtro de EF y RLS. El worker abre una transacción por vuelta y fija `app.operador_id` para cada operador, así que cada cálculo solo ve los datos de ese operador.

**Sin duplicados aunque haya varias instancias (6.13):**
- la tabla tiene un índice único por (operador, día) y el cálculo es un `INSERT ... ON CONFLICT DO UPDATE`: correrlo dos veces deja la misma fila con los valores al día;
- además, cada vuelta toma un `pg_try_advisory_xact_lock`: si otra instancia del worker está calculando, esta se saltea la vuelta.

El Backoffice muestra los cierres del período en **Reportes → Cierre diario**. Lo prueba `CierreDiarioTests`: con un reloj fijo, corre el cierre dos veces y verifica que queda una fila por día y operador, con los números correctos y el día anterior cerrado.
