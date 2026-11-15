# LastMileUY — Despliegue y recuperación en Azure

Guía operativa de `scripts/deploy/` para Windows PowerShell 5.1 y una VM Linux con Docker Compose. El punto de entrada es **`main-azure.ps1`**, que coordina el flujo de reconstrucción mediante `recrear-azure.ps1`.

> **Importante:** ejecutar los comandos desde la raíz del repositorio. Un `destroy` elimina recursos de Azure y solo debe comenzar después de validar el entorno y completar un respaldo recuperable fuera de la VM.

## Requisitos

En la PC desde la que se ejecuta el despliegue:

- Windows PowerShell 5.1 o superior.
- Git, con acceso al repositorio LastMileUY y a `origin/main`.
- Azure CLI (`az`), con sesión iniciada y la **suscripción correcta** seleccionada.
- Terraform, con la infraestructura de `infra/terraform/` inicializada, `terraform.tfvars` correspondiente y estado vigente. **No borrar `terraform.tfstate` ni perder el backend de estado.**
- Docker Desktop funcionando, con `docker compose` disponible. Se usa Docker para validar la sintaxis Bash sin depender de WSL.
- Cliente OpenSSH: `ssh` y `scp` disponibles.
- Conectividad a Azure, GitHub/GHCR y la VM.
- Archivo privado `$HOME\LastMileUY-Azure\config\.env.azure` y clave SSH `$HOME\LastMileUY-Azure\ssh\lastmileuy_azure_nueva`.
- Imágenes GHCR correspondientes a la versión que se va a instalar y commit accesible en GitHub.
- Espacio local suficiente para guardar los respaldos **fuera de la VM**.

No versionar secretos, archivos de estado, respaldos ni claves privadas.

## 1. Preflight: validación sin destruir

Ejecutar manualmente:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy\validar-preflight-local.ps1
```

Resultado esperado: **`PREFLIGHT_LOCAL_OK`**.

El preflight comprueba:

1. Herramientas locales: Git, Azure CLI, Terraform, Docker, SSH y SCP.
2. Repositorio Git, rama esperada y ausencia de cambios locales rastreados que puedan provocar un despliegue no reproducible.
3. Sesión de Azure y suscripción activa. **Verificar que la suscripción mostrada sea la del despliegue**; estar autenticado no garantiza haber elegido la correcta.
4. Docker Desktop operativo.
5. Sintaxis de los scripts PowerShell y Bash (estos últimos mediante un contenedor Docker).
6. Existencia de archivos esenciales para el despliegue.
7. `terraform init`, `terraform validate` y `terraform plan`. Para el entorno desplegado normalmente se exige **`No changes`**; si el plan muestra cambios, detenerse y revisarlos.
8. Validez de Docker Compose usando variables temporales de prueba, sin modificar las credenciales reales.

**El preflight se ejecuta automáticamente al iniciar `main-azure.ps1 -Destroy`, antes del respaldo o de modificar infraestructura.** También puede ejecutarse por separado.

Si alguna comprobación falla, el proceso debe cancelarse. El preflight no demuestra que el respaldo pueda restaurarse y no reemplaza las verificaciones de recuperación.

## 2. Reconstrucción completa desde cero

Con `main` actualizado, el repositorio limpio y el preflight aprobado:

```powershell
.\scripts\deploy\main-azure.ps1 -Destroy
```

Flujo esperado:

1. Preflight local automático.
2. Respaldo nuevo de PostgreSQL, roles, RabbitMQ, volúmenes y metadatos necesarios; descarga y verificación SHA256.
3. Confirmación explícita `DESTRUIR`.
4. `terraform destroy` y posterior `terraform apply`.
5. Instalación de la nueva VM y los servicios.
6. Restauración de PostgreSQL, roles, RabbitMQ y volúmenes.
7. Arranque de contenedores y configuración de Caddy/HTTPS.
8. Comprobaciones finales y, **solo si el actualizador es seguro**, activación de `lastmileuy-update.timer`.

**Antes de escribir `DESTRUIR`**, comprobar que el respaldo recién generado exista en `$HOME\LastMileUY-Azure\backups\AAAAMMDD-HHMMSS`, que haya superado la verificación de integridad y que permanezca fuera de los recursos que se van a destruir. Conservar también respaldos anteriores.

La recreación puede cambiar la IP pública. No asumir que seguirá siendo la misma.

### Datos y migraciones EF Core

El restaurador recupera la base tal como estaba en el respaldo, incluidas sus políticas RLS; no impone cantidades fijas de políticas ni modifica automáticamente su configuración.

La compatibilidad con EF Core se verifica mediante `--check-migrations`. **La restauración y el actualizador automático no deben aplicar migraciones pendientes.** Si hay migraciones nuevas, planificar su ejecución y recuperación por separado antes de desplegar esa versión.

El rollback de imágenes y código **no revierte automáticamente una migración ni restaura la base de datos**.

## 3. Si falla una reconstrucción

**No volver a ejecutar `-Destroy` automáticamente.** Revisar el log y el diario de reanudación:

```text
$HOME\LastMileUY-Azure\logs\reconstruccion-AAAAMMDD-HHMMSS.log
$HOME\LastMileUY-Azure\backups\.lastmileuy-reconstruccion-pendiente.json
```

Si el diario indica que puede reanudarse con seguridad, utilizar **la misma carpeta de respaldo**:

```powershell
.\scripts\deploy\main-azure.ps1 -ResumeBackupDir "$HOME\LastMileUY-Azure\backups\AAAAMMDD-HHMMSS"
```

La reanudación no debe repetir el `destroy` ni importar datos indiscriminadamente sobre una base existente. Si `pg_restore` o la extracción de un volumen quedaron a medias, inspeccionar el estado y decidir una recuperación controlada. **No borrar volúmenes ni forzar otra importación sin revisar.**

Si se cancela después del respaldo, pero antes del `destroy`, los servicios de la VM original pueden haber quedado detenidos temporalmente. Verificar su estado.

## 4. Resultado y accesos

Al finalizar correctamente, el proceso debería mostrar **`RESTAURACION_COMPLETA_OK`** y **`RECONSTRUCCION_Y_RESTAURACION_OK`**, junto con la IP nueva, el comando SSH y las URLs públicas.

```powershell
ssh -i "$HOME\LastMileUY-Azure\ssh\lastmileuy_azure_nueva" azureuser@IP_NUEVA
```

Para los dominios `sslip.io`, sustituir los puntos de la IP por guiones:

| Servicio | URL |
| --- | --- |
| API | `https://api.IP-CON-GUIONES.sslip.io` |
| Backoffice | `https://backoffice.IP-CON-GUIONES.sslip.io` |
| Portal comercio | `https://comercio.IP-CON-GUIONES.sslip.io` |
| App repartidor | `https://repartidor.IP-CON-GUIONES.sslip.io` |
| Seguimiento público | `https://seguimiento.IP-CON-GUIONES.sslip.io` |
| Receptor avisos | `https://receptor.IP-CON-GUIONES.sslip.io` |

En la reconstrucción comprobada el 15/11/2026 la IP fue `20.226.3.211`; **no usarla como constante** para las siguientes recreaciones.

## 5. Comprobaciones posteriores en la VM

Una vez conectado por SSH:

```bash
docker ps --format 'table {{.Names}}\t{{.Status}}'
docker exec lastmileuy-postgres pg_isready -U lastmileuy
docker exec lastmileuy-rabbitmq rabbitmqctl list_queues name messages
cat /var/lib/lastmileuy-deploy/version
git -C /opt/lastmileuy rev-parse HEAD
systemctl is-enabled lastmileuy-update.timer
systemctl is-active lastmileuy-update.timer
systemctl list-timers --all | grep lastmileuy
```

Comprobar que la versión registrada y el commit desplegado coincidan, PostgreSQL acepte conexiones y RabbitMQ conserve las colas esperadas. Verificar HTTPS, acceso a los módulos y **datos reales restaurados**; una respuesta HTTP 200 no demuestra por sí sola la integridad de los datos.

## 6. Actualizador automático y rollback

`actualizar-azure.sh` consulta la versión publicada y realiza actualizaciones controladas. La versión revisada **bloquea despliegues con migraciones EF Core pendientes**, en vez de aplicar `--apply-migrations` automáticamente.

El rollback intenta recuperar el código, la configuración Compose y las imágenes anteriores. **No revierte la base de datos**.

Tras una reconstrucción, `restaurar-vm.sh` debe habilitar `lastmileuy-update.timer` **solo si** comprueba que el actualizador instalado es la versión segura, que protege frente a migraciones pendientes y que su sintaxis Bash es válida. Si no supera esas comprobaciones, debe dejar el timer deshabilitado y avisar.

Para inspeccionarlo:

```bash
systemctl status lastmileuy-update.timer --no-pager
grep -nE 'BLOQUEADO|apply-migrations|trap rollback_si_falla' /opt/lastmileuy/scripts/deploy/actualizar-azure.sh
```

**No habilitar manualmente** un timer que ejecute un actualizador antiguo capaz de aplicar migraciones automáticamente.

## 7. Archivos principales

| Archivo | Función |
| --- | --- |
| `main-azure.ps1` | Punto de entrada, preflight automático y logs |
| `validar-preflight-local.ps1` | Comprobaciones locales antes del destroy |
| `recrear-azure.ps1` | Orquestación de Terraform y reanudación |
| `respaldar-azure.ps1` | Respaldo y descarga verificada |
| `restaurar-azure.ps1` | Validación, envío y restauración remota |
| `instalar-azure.sh` | Preparación de la nueva VM |
| `restaurar-vm.sh` | Recuperación de datos y servicios |
| `configurar-caddy.sh` | HTTPS y dominios `sslip.io` |
| `actualizar-azure.sh` | Actualización automática y rollback |
| `lastmileuy-update.service` / `.timer` | Ejecución periódica del actualizador |
| `diagnosticar-vm.sh` | Diagnóstico remoto |

## 8. Seguridad y costos

- No versionar `.env.azure`, claves SSH, `terraform.tfvars` con secretos, `terraform.tfstate`, dumps de PostgreSQL ni carpetas de respaldo.
- No ejecutar `terraform destroy` manualmente fuera del flujo de respaldo y recuperación.
- Revisar la suscripción Azure seleccionada antes de confirmar la destrucción.
- Conservar respaldos independientes de la VM y verificar que puedan recuperarse.
- Azure puede facturar VM, discos, IP pública y transferencia; destruir y recrear **no garantiza costo cero**.
