output "resource_group_name" {
  description = "Grupo de recursos"
  value       = azurerm_resource_group.lastmileuy.name
}

output "vm_name" {
  description = "Nombre de la VM"
  value       = azurerm_linux_virtual_machine.lastmileuy.name
}

output "vm_id" {
  description = "Identificador de la VM"
  value       = azurerm_linux_virtual_machine.lastmileuy.id
}

output "public_ip" {
  description = "IP publica de LastMileUY"
  value       = azurerm_public_ip.lastmileuy.ip_address
}

output "ssh_command" {
  description = "Comando SSH orientativo"
  value       = "ssh -i ~/.ssh/lastmileuy_azure ${var.admin_username}@${azurerm_public_ip.lastmileuy.ip_address}"
}