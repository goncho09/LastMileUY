resource "azurerm_linux_virtual_machine" "lastmileuy" {
  name                = var.vm_name
  resource_group_name = azurerm_resource_group.lastmileuy.name
  location            = azurerm_resource_group.lastmileuy.location

  size = var.vm_size

  admin_username                  = var.admin_username
  disable_password_authentication = true

  network_interface_ids = [
    azurerm_network_interface.lastmileuy.id
  ]

  admin_ssh_key {
    username   = var.admin_username
    public_key = trimspace(var.ssh_public_key)
  }

  os_disk {
    name                 = "lastmileuy-osdisk"
    caching              = "ReadWrite"
    storage_account_type = "Standard_LRS"
    disk_size_gb         = var.os_disk_size_gb
  }

  source_image_reference {
    publisher = "Canonical"
    offer     = "ubuntu-24_04-lts"
    sku       = "server"
    version   = "latest"
  }

  custom_data = base64encode(
    file("${path.module}/cloud-init.yaml")
  )

  boot_diagnostics {}

  tags = var.tags
}