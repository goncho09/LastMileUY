# Seguridad

Este documento junta en un solo lugar los controles de seguridad del sistema y por qué están. El detalle de cada módulo está en su propio documento.

## Quién es quién: autenticación

- **Usuarios y contraseñas:** ASP.NET Core Identity. La contraseña pide al menos 8 caracteres, con mayúscula, minúscula y número.
- **Bloqueo por intentos:** el login usa `lockoutOnFailure: true`. Después de 5 contraseñas mal, la cuenta queda bloqueada 5 minutos (valores por defecto de Identity). La respuesta siempre es un `401` igual, sin decir si el email existe, si está bloqueada o si está inactiva.
- **Tope de intentos por visitante:** el bloqueo por cuenta no frena que alguien pruebe una contraseña contra muchas cuentas. Por eso el Backoffice y el portal aceptan hasta 10 envíos del login por minuto y por IP (`Login:IntentosPorMinuto`). Al pasarse responden `429` con un mensaje. En el portal, la elección de operador comparte ese cupo, porque también inicia la sesión.
  - El tope va en cada aplicación y no en la API porque a la API todos los intentos le llegan desde la IP del servidor del Backoffice o del portal.
  - Detrás de un proxy inverso, todos los visitantes llegarían con la IP del proxy: habría que configurar `ForwardedHeaders` para tomar la IP real.
- **Sesión:** cookie `LastMileUY.Auth` con estas propiedades:
  - `HttpOnly`: JavaScript no la puede leer.
  - `Secure`: solo viaja por https.
  - `SameSite=Lax`: no viaja en un POST desde otro sitio, lo que frena el CSRF contra la API. Pasa a `None` solo si se configura `AppRepartidor:Origenes`, en desarrollo.
  - Dura 8 horas y se renueva con el uso.
- **Usuarios desactivados:** la sesión se revalida cada minuto (`UsuarioSecurityStampValidator`). Si se desactiva un usuario, pierde el acceso en ese plazo aunque tenga la cookie.
- **Usuario de comercio con varios operadores:** elige con cuál trabaja al iniciar sesión. El operador elegido se valida contra la tabla `OperadoresComercios` y se guarda solo en la cookie. El token temporal para elegirlo dura 5 minutos y se usa una sola vez.
- **Backoffice y portal:** cada uno tiene su propia cookie cifrada, y adentro va la cookie de la API, que reenvían en cada llamada. El `HttpClient` no guarda cookies (`UseCookies = false`), así que la sesión de un usuario no se le puede mandar a otro.

## Qué puede hacer cada uno: autorización

Cada endpoint declara qué perfiles lo pueden usar (`RequireRole`). Los perfiles son:

| Perfil | Qué hace |
|---|---|
| Operador | Todo lo del operador, incluida su configuración |
| Despachador | Envíos y hojas de ruta; no ve la configuración |
| OperarioDeposito | Consulta envíos |
| Repartidor | Solo sus hojas de ruta y la sincronización de sus entregas |
| Comercio | Sus envíos, destinatarios y suscripciones a avisos |

Además de mirar el perfil, también se controla de quién es cada recurso. La hoja de ruta tiene que ser del repartidor que la pide, y la ruta de una operación sincronizada también.

**Minimización de datos personales:**
- El repartidor recibe lo justo para entregar: nombre, teléfono y dirección, sin el email.
- El listado de destinatarios es solo para quien da de alta envíos (Comercio y Operador).

**Métricas del caché:** son de toda la plataforma. Las ve el perfil Operador en el tablero técnico, pero solo el Administrador las pone en cero (`POST /api/cache/metricas/reiniciar`). Si las reiniciara un operador, les borraría la medición a los demás.

## Aislamiento entre operadores

Ver el README (ADR-01). El aislamiento tiene dos capas:
- un filtro global de EF;
- RLS en PostgreSQL, con un usuario de base (`lastmileuy_app`) que no puede saltearlo.

Las pruebas de `tests/LastMileUY.API.Tests/Multitenancy` comprueban que un operador no ve datos de otro, ni un comercio los de otro comercio.

## Entradas

- **Límites:** las minimal APIs no validan solas los `[MaxLength]` y `[Range]` de los contratos, así que cada servicio controla largos y rangos. Un dato fuera de rango devuelve un `400` con el motivo, nunca un `500` de la base.
- **Paginación:** el tamaño de página tiene un máximo y la página un tope, para que el salto no desborde.
- **Imágenes de evidencia:** se revisa que la firma y la foto sean PNG o JPEG por sus primeros bytes, no por lo que diga el pedido, y que pesen hasta 1 MB. Se guardan con un nombre que genera el servidor, nunca con el que manda el cliente.
- **Consultas:** todo pasa por EF Core o por SQL parametrizado (`NpgsqlParameter`). Las búsquedas con `ILIKE` escapan los comodines.

## Avisos HTTP a comercios (SSRF)

La URL de un aviso la escribe el comercio, pero la llama nuestro servidor desde adentro de la red. Por eso:
- solo se acepta https, sin usuario ni contraseña en la URL y con un máximo de 2000 caracteres;
- solo se aceptan direcciones públicas. La IP se revisa también **al conectarse**, porque un dominio público puede resolver a una IP interna;
- no se siguen redirecciones y hay un tiempo máximo de 10 segundos.

`Avisos:PermitirDestinosLocales` saca estos controles y es **solo para desarrollo**. Detalle en [mensajeria.md](mensajeria.md).

## Endpoint público de seguimiento

`GET /api/seguimiento/{codigo}` no pide sesión:
- el código es aleatorio, de 12 caracteres, y el `Id` del envío nunca se expone;
- solo devuelve lo necesario para seguir el envío;
- la base solo deja leer ese envío, con una política RLS propia;
- tiene un tope de consultas por minuto y por IP (`429` al pasarlo).

Detalle en [seguimiento-publico.md](seguimiento-publico.md).

## Navegador

- **Redirecciones después del login:** solo a direcciones del propio sitio.
  - El Backoffice usa `Url.IsLocalUrl`.
  - El portal descarta las que no empiezan con `/` o empiezan con `//` o `/\`. Además, `NavigationManager` arma siempre la dirección completa sobre el host del portal. Por eso un `ReturnUrl` con trucos como `/<tab>/otro-sitio` termina en una ruta del propio portal: comprobado, .NET lo convierte en `http://<portal>/%09/otro-sitio`.
- **Encabezados de seguridad:** todas las aplicaciones mandan `X-Content-Type-Options: nosniff`, para que el navegador no interprete un archivo como otro tipo. Además:

  | Aplicación | `Referrer-Policy` | Iframes |
  |---|---|---|
  | Backoffice, portal, app del repartidor | `same-origin` | `X-Frame-Options` |
  | Seguimiento público | `no-referrer` | `DENY` |

  En el seguimiento, la dirección lleva el código del envío y el logo del operador puede venir de otro sitio, así que no se le manda la dirección a nadie. nginx tampoco muestra su versión (`server_tokens off`).
- **Formularios:** el Backoffice (Razor Pages) y el portal (`UseAntiforgery`) validan el token antiforgery en los POST.
- **App del repartidor:** guarda los datos del celular cifrados con AES-GCM, los borra al cerrar sesión y no guarda nada más de 24 horas. El service worker nunca guarda respuestas de `/api/`. Detalle en [datos-locales.md](datos-locales.md).
- **Script de SignalR en el Backoffice:** se carga del CDN con integridad (SRI). Si el archivo cambia, el navegador no lo ejecuta.

## Pendientes conocidos

- **Despliegue:** las contraseñas de los usuarios de base y las variables que hay que cambiar están en la sección "Antes de desplegar" del README.
