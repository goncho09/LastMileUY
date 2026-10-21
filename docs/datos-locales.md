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
4. **De un solo usuario.**
   - Al cerrar sesión se borra todo, la clave incluida, antes de avisarle a la API.
   - Al iniciar sesión, si lo guardado no es de ese usuario (o no se sabe de quién es), se borra todo.
5. **Solo lo necesario.**
   - La API no le manda al repartidor el email del destinatario (`ConsultasRutasService.ObtenerHojaParaLaAppAsync`): para comunicarse alcanza el teléfono.
   - Una operación pendiente se borra apenas la API la acepta, la rechaza o la marca en conflicto, porque la firma y la foto ya quedaron en el servidor.

## Qué protege y qué no

| Situación | Protegido |
|---|---|
| Otro repartidor usa el mismo celular | Sí: al iniciar sesión se borra lo del anterior. |
| Alguien revisa el almacenamiento del navegador (herramientas de desarrollo, una extensión, una copia de la carpeta del perfil) | Sí: ve datos cifrados, y la clave no se puede exportar. |
| Se pierde o se roba el celular con la sesión abierta | En parte: los datos vencen en un día, como máximo. Además depende del bloqueo de pantalla del celular, que no controla la app. |
| Código malicioso ejecutándose dentro de la app | No: puede usar la clave igual que la app. Lo cubren las protecciones de la app web (sin scripts de terceros, todo servido desde el mismo origen). |

## Para el modo sin conexión

- Guardar la hoja con `GuardarAsync("hoja:{rutaId}", hoja, ...)` y una duración que no pase el fin de la jornada.
- Guardar cada operación pendiente por su `IdOperacion` y borrarla con `BorrarAsync` cuando la API devuelva un resultado final.
- Antes de cerrar sesión, si quedan operaciones pendientes, avisarle al repartidor: al cerrar sesión se borran.
