# LastMileUY - restauracion Azure revisada

Este paquete reemplaza/añade los archivos de `scripts/deploy` y el override `docker-compose.azure.yml`.

## Archivos

- `scripts/deploy/main-azure.ps1`: entrada principal, delega en `recrear-azure.ps1`.
- `scripts/deploy/recrear-azure.ps1`: preflight, backup nuevo, ventana de mantenimiento, confirmacion y reconstruccion.
- `scripts/deploy/respaldar-azure.ps1`: respaldo remoto existente, conservado.
- `scripts/deploy/restaurar-azure.ps1`: valida hashes, archivos, version, fecha, colas y hostname Mnesia antes de conectarse a Azure.
- `scripts/deploy/restaurar-vm.sh`: restaura PostgreSQL, RLS, RabbitMQ y volumenes; verifica servicios, endpoints y consumidores del Worker.
- `scripts/deploy/instalar-azure.sh`, `configurar-caddy.sh`, unidades systemd.
- `docker-compose.azure.yml`: fija el hostname de RabbitMQ a partir de `RABBITMQ_RESTORE_HOST`.

## Uso

Desde la raiz del repositorio:

```powershell
# Solo preflight: Terraform init/validate/plan y validacion del ultimo backup disponible.
.\scripts\deploy\main-azure.ps1

# Backup nuevo + validaciones + confirmacion + destroy/recreate/restauracion.
.\scripts\deploy\main-azure.ps1 -Destroy
```

El modo destructivo queda bloqueado por defecto. Para continuar sin aceptar costos, primero verifica en Azure Portal que el tamaño exacto de VM este cubierto por tu suscripcion Azure Free y pasa `-SkuAzureFreeVerificada` con el mismo valor exacto de `vm_size`. El script no mantiene una lista supuestamente universal de SKU gratuitas porque elegibilidad, cuota y region dependen de la suscripcion. `-PermitirSkuConCosto` existe solo para aceptar expresamente costos; no usarlo si el requisito es permanecer en Azure Free.

El flujo no ejecuta cambios en Azure durante el preflight. `-Destroy` crea un backup fresco; el proceso remoto detiene las aplicaciones, RabbitMQ, Grafana, Prometheus y el timer de actualizaciones y los deja detenidos tras validar el respaldo. Si la descarga o validacion local falla, intenta reanudarlos. Si el usuario cancela, tambien los reanuda. Solo despues de confirmar se ejecuta Terraform.

## Importante

- No ejecutar desde una carpeta de trabajo que no contenga el estado Terraform correcto.
- No subir `terraform.tfstate`, `terraform.tfvars`, `.env.azure`, claves SSH ni backups a Git.
- El backup y la restauracion deben probarse localmente con el mismo conjunto de imagenes y volumenes.
- La restauracion solo habilita el timer de actualizaciones despues de completar todas las comprobaciones.
- Las pruebas locales no demuestran por si solas que la VM nueva tenga cuota, disponibilidad de SKU o ausencia de costos.
