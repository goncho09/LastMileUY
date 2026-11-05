variable "subscription_id" {
  description = "ID de la suscripcion Azure for Students"
  type        = string
  sensitive   = true
}

variable "location" {
  description = "Region de Azure"
  type        = string
  default     = "brazilsouth"
}

variable "resource_group_name" {
  description = "Nombre del grupo de recursos"
  type        = string
  default     = "lastmileuy-rg"
}

variable "vm_name" {
  description = "Nombre de la maquina virtual"
  type        = string
  default     = "lastmileuy-server"
}

variable "vm_size" {
  description = "Tamano de la VM"
  type        = string
  default     = "Standard_D2as_v4"
}

variable "admin_username" {
  description = "Usuario administrador de Ubuntu"
  type        = string
  default     = "azureuser"
}

variable "ssh_public_key" {
  description = "Contenido de la clave publica SSH"
  type        = string
}

variable "ssh_allowed_cidr" {
  description = "IP publica autorizada para SSH, formato x.x.x.x/32"
  type        = string

  validation {
    condition     = can(cidrhost(var.ssh_allowed_cidr, 0))
    error_message = "Debe ser un CIDR valido, por ejemplo 203.0.113.10/32."
  }
}

variable "os_disk_size_gb" {
  description = "Tamano del disco del sistema"
  type        = number
  default     = 64
}

variable "tags" {
  description = "Etiquetas para identificar los recursos"
  type        = map(string)

  default = {
    project     = "LastMileUY"
    environment = "academic"
    managed_by  = "terraform"
  }
}