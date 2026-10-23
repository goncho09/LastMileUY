# Datos personales guardados en el celular del repartidor

Para trabajar sin conexión, la app del repartidor tiene que guardar en el celular la hoja de ruta
(nombre, dirección y teléfono de cada destinatario) y las operaciones que todavía no mandó (con la
firma, la foto y el documento de quien recibió). Son datos personales de terceros (Ley 18.331 de
protección de datos personales). Este documento dice cómo se protegen.

Código: `src/LastMileUY.DriverPWA/Services/AlmacenLocal.cs` y `wwwroot/js/almacen.js`.
Pruebas: `tests/LastMileUY.DriverPWA.Tests/AlmacenLocalTests.cs`.

## Reglas

1. **Todo lo que se guarde pasa por `AlmacenLocal`.** Nada de `localStorage`, `sessionStorage` ni
   IndexedDB directo: ahí quedaría en texto plano.
2. **Cifrado.** Cada dato se guarda en IndexedDB cifrado con AES-GCM de 256 bits, con un IV nuevo cada
   vez. La clave la genera el navegador (Web Crypto) como **no exportable**: el código de la app la
   puede usar para cifrar y descifrar, pero nadie la puede leer, ni siquiera la app.
3. **Vencimiento.** Cada dato se guarda con una duración, como máximo 24 horas (`DuracionMaxima`). Un
   dato vencido no se devuelve y se borra al intentar leerlo.
   **Excepción: las operaciones pendientes** (`GuardarPendienteAsync`). Una entrega registrada sin
   conexión no vence: si se borrara sola, el envío quedaría entregado en la calle y sin registrar en el
   sistema, y la firma de quien recibió no se puede volver a pedir. El dato de quién es el dueño de lo
   guardado tampoco vence, porque es el que permite conservar esas pendientes. Siguen cifradas, y salen
   del celular con un resultado final de la API o si entra otro repartidor (regla 4). La app las
   reenvía sola al abrirse y cada vez que vuelve la conexión, en cualquier pantalla (`ReenvioAutomatico`).
4. **De un solo usuario.**
   - Con operaciones sin enviar no se puede cerrar sesión: la app avisa cuántas son, para no perderlas.
   - Al cerrar sesión se borra todo, la clave incluida, cuando la API confirma el cierre. Si la API
     responde que la sesión ya había vencido (401) también se borra. Sin conexión no se borra nada y
     el repartidor lo vuelve a intentar.
   - Al iniciar sesión, si lo guardado no es de ese usuario (o no se sabe de quién es), se borra todo.
5. **Solo lo necesario.**
   - La API no le manda al repartidor el email del destinatario (`ConsultasRutasService.ObtenerHojaParaLaAppAsync`): para comunicarse alcanza el teléfono.
   - Una operación pendiente se borra apenas la API la acepta, la rechaza o la marca en conflicto, porque la firma y la foto ya quedaron en el servidor.

## Qué protege y qué no

| Situación | Protegido |
|---|---|
| Otro repartidor usa el mismo celular | Sí: al iniciar sesión se borra lo del anterior. |
| Alguien revisa el almacenamiento del navegador (herramientas de desarrollo, una extensión, una copia de la carpeta del perfil) | Sí: ve datos cifrados, y la clave no se puede exportar. |
| Se pierde o se roba el celular con la sesión abierta | En parte: la hoja y los motivos vencen en un día, como máximo; las operaciones sin enviar quedan hasta que se sincronicen. Además depende del bloqueo de pantalla del celular, que no controla la app. |
| Código malicioso ejecutándose dentro de la app | No: puede usar la clave igual que la app. Lo cubren las protecciones de la app web (sin scripts de terceros, todo servido desde el mismo origen). |

## Qué guarda la app

| Qué | Dónde (código) | Vence |
|---|---|---|
| Mis rutas y el detalle de cada una (`driver:mis-rutas`, `driver:ruta:{id}`) | `RutasService` | 24 horas |
| Motivos de no entrega (`driver:motivos`) | `SincronizacionService` | 24 horas |
| Operaciones sin enviar, con su `IdOperacion` (`driver:operaciones-pendientes`) | `ColaOperacionesService` | No: salen con un resultado final de la API |
| Dueño de lo guardado (`dueno`) | `AlmacenLocal` | No |

Las operaciones sin enviar se ven en la pantalla "Mis operaciones" (`/operaciones`).
