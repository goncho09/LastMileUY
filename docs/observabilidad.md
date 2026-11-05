# Observabilidad

Registro estructurado con **Serilog** y métricas y trazas con **OpenTelemetry** (letra 6.12). Todo corre en
el `compose.yaml`:

| Herramienta | Dirección | Para qué |
|---|---|---|
| Grafana | http://localhost:3001 (admin / admin) | El tablero técnico: **LastMileUY — Observabilidad** |
| Prometheus | http://localhost:9090 | Guarda las métricas (la API y el worker se las mandan por OTLP; RabbitMQ se lee) |
| Jaeger | http://localhost:16686 | Trazas de punta a punta, con el identificador de correlación |

El tablero está en `observabilidad/grafana/` y Grafana lo carga al arrancar. Si se cambia desde la pantalla
de Grafana, hay que exportarlo (Export → Export as JSON) y reemplazar el archivo: si no, al reiniciar vuelve
el del repo.

## Qué muestra el tablero técnico

| Pide la letra (6.12) | Gráfica | De dónde sale |
|---|---|---|
| Tiempo medio de respuesta | Latencia HTTP promedio — API | `http_server_request_duration_seconds` (ASP.NET Core) |
| Percentiles 95 y 99 | Latencia HTTP p95 y p99 — API | El mismo histograma |
| Tasa de errores | Tasa de errores HTTP — API | Respuestas 5xx sobre el total |
| Profundidad de la cola | Profundidad de las colas — RabbitMQ | Plugin `rabbitmq_prometheus` (`observabilidad/rabbitmq/enabled_plugins`): mensajes en la cola del worker y en la de fallidos |
| Sincronizaciones pendientes | Sincronización — operaciones por resultado / tiempo pendiente en el celular | `TelemetriaSincronizacion` (API) |
| Avisos a comercios fallidos | Avisos a comercios fallidos / Intentos de avisos | `TelemetriaAvisos` (worker) |
| Relación de aciertos del caché | Relación de aciertos del caché / Aciertos en el tiempo, por caso | `TelemetriaCache` (API) |

Además: solicitudes por segundo, CPU, memoria y excepciones de la API y el worker.

**Sincronizaciones pendientes.** Lo que todavía está en un celular sin conexión el servidor no lo puede
ver. Lo que sí se mide es cada operación cuando llega: cuántas por resultado (aplicadas, rechazadas, en
conflicto) y **cuánto estuvieron pendientes en el celular** (de que el repartidor la registró a que llegó a la
API). Con el repartidor sin conexión, esa demora crece.

**Caché.** Los aciertos y fallos se cuentan dos veces: en Redis (para el tablero técnico del Backoffice, de
toda la plataforma) y como métrica de OpenTelemetry (para Grafana). La de OpenTelemetry sigue contando aunque
Redis esté caído.

## Métricas propias

| Métrica (Prometheus) | Etiquetas | Dónde se cuenta |
|---|---|---|
| `lastmileuy_cache_lecturas_total` | `caso`, `resultado` (acierto, fallo) | `Infrastructure/Cache/TelemetriaCache` |
| `lastmileuy_avisos_intentos_total` | `resultado` (entregado, fallido) | `Worker/TelemetriaAvisos` |
| `lastmileuy_avisos_fallidos_total` | — | `Worker/TelemetriaAvisos` (avisos que agotaron sus intentos) |
| `lastmileuy_sincronizacion_operaciones_total` | `resultado` (Aplicada, Rechazada, Conflicto) | `Modules/Sincronizacion/Application/TelemetriaSincronizacion` |
| `lastmileuy_sincronizacion_demora_seconds` | (histograma) | `TelemetriaSincronizacion` |

Las métricas propias se crean en 0 al arrancar la API y el worker (`Inicializar`). Prometheus calcula los
aumentos comparando dos muestras: si una serie apareciera recién con el primer evento (ya en 1), ese
evento no se vería en el tablero. Por lo mismo, la demora de sincronización arranca con una medición en 0.
Las aplicaciones mandan las métricas cada minuto, así que las gráficas tardan hasta un minuto en moverse.
