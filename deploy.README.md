# LastMileUY — Despliegue y recuperación en Azure

Guía operativa de `scripts/deploy/` para Windows PowerShell 5.1 y una VM Linux con Docker Compose. El punto de entrada es **`main-azure.ps1`**, que delega en `recrear-azure.ps1`.

## Requisitos

- PowerShell 5.1+, Terraform, Docker Compose, SSH/SCP y acceso a Azure.
- Infraestructura configurada en `infra/terraform/` con su `terraform.tfvars` y estado existente. **No borrar `terraform.tfstate`.**
- Archivo privado `$HOME\LastMileUY-Azure\config\.env.azure` y clave `$HOME\LastMileUY-Azure\ssh\lastmileuy_azure_nueva`.
- Repositorio y versiones de imágenes GHCR disponibles. Para una nueva VM, el commit respaldado debe ser accesible.
- Copias de seguridad guardadas fuera de la VM.

Ejecutar los comandos desde la **raíz del repositorio**.

## 1. Validación sin destruir

## 1. Validación previa al despliegue

Ejecutar manualmente:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy\validar-preflight-local.ps1
```

Resultado esperado: `PREFLIGHT_LOCAL_OK`.

El preflight verifica:

- Herramientas locales: PowerShell, Git, Azure CLI, Terraform, Docker, SSH y SCP.
- Repositorio Git y estado de los cambios locales.
- Autenticación y suscripción activa de Azure.
- Funcionamiento de Docker Desktop.
- Sintaxis de scripts PowerShell y Bash.
- Existencia de archivos esenciales para el despliegue.
- Inicialización y validación de Terraform.
- `terraform plan`, exigiendo que no existan cambios de infraestructura pendientes.
- Configuración de Docker Compose con variables temporales de validación.

**El preflight se ejecuta automáticamente al iniciar `main-azure.ps1 -Destroy`, antes de realizar el respaldo o modificar infraestructura.**

También puede ejecutarse independientemente para comprobar el entorno sin destruir recursos.

Si alguna comprobación falla, el proceso se cancela.

El preflight no garantiza la recuperación de los datos ni sustituye las verificaciones del respaldo.

## 2. Reconstrucción completa desde cero

```powershell
.\scripts\deploy\main-azure.ps1 -Destroy
```

Flujo: preflight → respaldo nuevo y verificación SHA256 → confirmación explícita `DESTRUIR` → `terraform destroy` → `terraform apply` → instalación → recuperación de PostgreSQL, roles, RabbitMQ y volúmenes → contenedores → Caddy/HTTPS → comprobaciones finales.

**Antes de escribir `DESTRUIR`**, confirmar que el respaldo nuevo existe en `$HOME\LastMileUY-Azure\backups\AAAAmmdd-HHMMSS` y fue validado. Conservar también respaldos anteriores. La reconstrucción cambia la IP pública; no asumir que será la misma.

### Datos y migraciones

El restaurador recupera la configuración de la base **tal como está en el respaldo**, incluidas las políticas RLS; no intenta corregirlas ni exige cantidades fijas. Verifica compatibilidad con las migraciones EF Core mediante `--check-migrations`, sin aplicar nuevas migraciones durante la restauración.

## 3. Si falla una reconstrucción

**No volver a ejecutar `-Destroy`.** Consultar el log y el diario de reanudación:

```text
$HOME\LastMileUY-Azure\logs\reconstruccion-AAAAmmdd-HHmmss.log
$HOME\LastMileUY-Azure\backups\.lastmileuy-reconstruccion-pendiente.json
```

Usar la **misma carpeta de respaldo** indicada por el diario:

```powershell
.\scripts\deploy\main-azure.ps1 -ResumeBackupDir "$HOME\LastMileUY-Azure\backups\AAAAmmdd-HHMMSS"
```

El flujo de reanudación no debe repetir `destroy` ni importar encima de una base existente. Si una extracción de volumen o `pg_restore` quedó a medias, detenerse y recuperar manualmente: **no borrar volúmenes ni forzar otra importación**.

Si se cancela después del backup pero antes del destroy, los servicios de la VM original pueden quedar detenidos. Revisar su estado antes de asumir que sigue atendiendo tráfico.

## 4. Resultado y accesos

Al terminar correctamente, el script imprime `RECONSTRUCCION_Y_RESTAURACION_OK`, la IP nueva, el comando SSH y las URLs. El patrón es:

```powershell
ssh -i "$HOME\LastMileUY-Azure\ssh\lastmileuy_azure_nueva" azureuser@IP_NUEVA
```

Sustituir los puntos de la IP por guiones para los dominios `sslip.io`:

| Servicio | URL |
| --- | --- |
| API | `https://api.IP-CON-GUIONES.sslip.io` |
| Backoffice | `https://backoffice.IP-CON-GUIONES.sslip.io` |
| Portal comercio | `https://comercio.IP-CON-GUIONES.sslip.io` |
| App repartidor | `https://repartidor.IP-CON-GUIONES.sslip.io` |
| Seguimiento público | `https://seguimiento.IP-CON-GUIONES.sslip.io` |
| Receptor avisos | `https://receptor.IP-CON-GUIONES.sslip.io` |

Para la reconstrucción comprobada el 15/11/2026, la IP fue `20.226.3.211`; **no usarla como constante** para próximas recreaciones.

## 5. Comprobaciones posteriores en la VM

```bash
docker ps --format 'table {{.Names}}\t{{.Status}}'
docker exec lastmileuy-postgres pg_isready -U lastmileuy
docker exec lastmileuy-rabbitmq rabbitmqctl list_queues name messages
cat /var/lib/lastmileuy-deploy/version
git -C /opt/lastmileuy rev-parse HEAD
systemctl is-enabled lastmileuy-update.timer
systemctl is-active lastmileuy-update.timer
```

Verificar que la versión registrada y el commit coincidan, que PostgreSQL acepte conexiones y que RabbitMQ conserve las colas esperadas. También revisar HTTPS, funcionamiento real de los módulos y datos relevantes; un `HTTP 200` por sí solo no demuestra que todos los datos estén recuperados.

## 6. Actualizador automático y rollback

`actualizar-azure.sh` consulta la versión publicada en GHCR. La versión revisada **bloquea** despliegues con migraciones EF Core pendientes; esas migraciones requieren una intervención planificada. El rollback intenta recuperar las imágenes y el código anteriores, **no revierte automáticamente la base de datos**.

La restauración habilita `lastmileuy-update.timer` **solo si** encuentra el actualizador revisado, detecta la protección contra migraciones y su sintaxis Bash es válida. Si no cumple estas condiciones, deja el timer deshabilitado y emite un aviso. **La coincidencia de versión Git no sustituye la comprobación del actualizador.**

Para inspeccionar sin habilitar:

```bash
systemctl status lastmileuy-update.timer --no-pager
grep -nE 'BLOQUEADO|apply-migrations|trap rollback_si_falla' /opt/lastmileuy/scripts/deploy/actualizar-azure.sh
```

No habilitar manualmente un timer que apunte a un actualizador antiguo que ejecute `--apply-migrations`.

## 7. Archivos principales

| Archivo | Función |
| --- | --- |
| `main-azure.ps1` | Entrada principal y transcripción de logs |
| `recrear-azure.ps1` | Orquestación, Terraform y reanudación |
| `respaldar-azure.ps1` | Respaldo y descarga verificada |
| `restaurar-azure.ps1` | Validación, envío y restauración remota |
| `instalar-azure.sh` | Preparación de la nueva VM |
| `restaurar-vm.sh` | Recuperación de datos y servicios |
| `configurar-caddy.sh` | HTTPS y dominios `sslip.io` |
| `actualizar-azure.sh` | Actualización automática y rollback |
| `lastmileuy-update.service` / `.timer` | Ejecución periódica del actualizador |
| `validar-preflight-local.ps1` | Validación local no destructiva |
| `diagnosticar-vm.sh` | Diagnóstico remoto |

## Seguridad y costos

No versionar `.env.azure`, claves SSH, `terraform.tfvars` con secretos, `terraform.tfstate`, dumps de PostgreSQL ni respaldos. No ejecutar `terraform destroy` manualmente fuera del flujo con respaldo. Azure factura recursos por uso y puede haber costos de discos, IP y transferencia; recrear no garantiza costo cero.
