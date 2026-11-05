# Caché distribuido (Redis)

La API guarda en Redis las consultas que se leen mucho y cambian poco. Redis es compartido: todas las
instancias de la API leen y escriben el mismo caché, y cuando una invalida un dato las demás ya no lo ven.

| Caso | Clave | Dura | Por qué |
|---|---|---|---|
| Configuración del operador | `lastmileuy:operador:{id}` | 1 hora | Se pide en cada inicio de sesión y casi nunca cambia |
| Seguimiento público | `lastmileuy:seguimiento:{código}` | 2 minutos | Mucho tráfico, el estado cambia pocas veces |

- **Lectura** (`CacheDistribuido.ObtenerOCrearAsync`): si está en Redis se devuelve (acierto); si no, se lee
  de la base y se guarda (fallo). No se guarda "no existe".
- **Cambios** (`InvalidacionCacheInterceptor`): al guardar un cambio del envío, de sus bultos o del operador
  se borra la clave, y la próxima lectura va a la base.
- **Métricas**: aciertos y fallos por caso, en Redis (tablero técnico del Backoffice) y en Prometheus
  (Grafana, ver [observabilidad.md](observabilidad.md)).
- Sin cadena de conexión `Redis` (pruebas, o la API fuera de Docker) se usa un caché en memoria con la misma interfaz.

## Si Redis se cae

El caché nunca deja a la API sin servicio: si Redis no responde, se consulta la base.

| Qué | Cómo se resuelve | Dónde |
|---|---|---|
| No esperar a Redis | Sin conexión, cada pedido a Redis falla en el acto (`BacklogPolicy.FailFast`) y se va a la base. Si Redis se cuelga sin cortar la conexión, se espera como mucho 1 segundo | `ConexionRedis` |
| Tablero técnico | La API contesta 503 "Redis no disponible" y el Backoffice muestra ese aviso | `CacheEndpoints`, `Tablero/Index` |
| Datos viejos al volver | Mientras Redis está caído las invalidaciones se pierden, y Redis guarda sus datos en disco: al volver traería datos viejos. Al reconectarse, la API borra lo cacheado (no los contadores) y lo siguiente se lee de la base | `LimpiezaCacheRedis` |

### Prueba manual (Docker)

```powershell
Invoke-RestMethod http://localhost:8080/api/seguimiento/<código>        # queda en el caché
docker compose stop redis
Measure-Command { Invoke-RestMethod http://localhost:8080/api/seguimiento/<código> }
# Backoffice > Tablero técnico: "Redis no está disponible…"
# Backoffice: cambiar el estado del envío
docker compose start redis
docker compose logs --tail 5 lastmileuy.api   # "Se volvió a conectar a Redis: se borraron N datos…"
Invoke-RestMethod http://localhost:8080/api/seguimiento/<código>        # muestra el estado nuevo
```

| | Antes | Después |
|---|---|---|
| Consulta de seguimiento con Redis caído | 11,8 s | menos de 1 s |
| Tablero técnico con Redis caído | "No se pudieron cargar las métricas" | "Redis no está disponible…" |
| Clave del caché después de reiniciar Redis | Seguía estando (dato posiblemente viejo) | Se borra al reconectar |

### Pruebas automáticas

`tests/LastMileUY.API.Tests/Cache/CacheConRedisTests.cs`, con un Redis real en Docker (Testcontainers):
dos instancias que comparten caché e invalidación, respuesta en menos de 1 s con Redis caído, métricas
que avisan que no están disponibles, y limpieza al volver la conexión (se corta con `CLIENT KILL`).
