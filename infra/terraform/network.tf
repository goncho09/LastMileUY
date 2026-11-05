resource "azurerm_virtual_network" "lastmileuy" {
  name                = "lastmileuy-vnet"
  address_space       = ["10.20.0.0/16"]
  location            = azurerm_resource_group.lastmileuy.location
  resource_group_name = azurerm_resource_group.lastmileuy.name

  tags = var.tags
}

resource "azurerm_subnet" "lastmileuy" {
  name                 = "lastmileuy-subnet"
  resource_group_name  = azurerm_resource_group.lastmileuy.name
  virtual_network_name = azurerm_virtual_network.lastmileuy.name
  address_prefixes     = ["10.20.1.0/24"]
}

resource "azurerm_public_ip" "lastmileuy" {
  name                = "lastmileuy-public-ip"
  location            = azurerm_resource_group.lastmileuy.location
  resource_group_name = azurerm_resource_group.lastmileuy.name

  allocation_method = "Static"
  sku               = "Standard"

  tags = var.tags
}

resource "azurerm_network_security_group" "lastmileuy" {
  name                = "lastmileuy-nsg"
  location            = azurerm_resource_group.lastmileuy.location
  resource_group_name = azurerm_resource_group.lastmileuy.name

  security_rule {
    name                       = "Allow-SSH"
    priority                   = 100
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "22"
    source_address_prefix      = var.ssh_allowed_cidr
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "Allow-HTTP"
    priority                   = 110
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "80"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  security_rule {
    name                       = "Allow-HTTPS"
    priority                   = 120
    direction                  = "Inbound"
    access                     = "Allow"
    protocol                   = "Tcp"
    source_port_range          = "*"
    destination_port_range     = "443"
    source_address_prefix      = "*"
    destination_address_prefix = "*"
  }

  tags = var.tags
}

resource "azurerm_subnet_network_security_group_association" "lastmileuy" {
  subnet_id                 = azurerm_subnet.lastmileuy.id
  network_security_group_id = azurerm_network_security_group.lastmileuy.id
}

resource "azurerm_network_interface" "lastmileuy" {
  name                = "lastmileuy-nic"
  location            = azurerm_resource_group.lastmileuy.location
  resource_group_name = azurerm_resource_group.lastmileuy.name

  ip_configuration {
    name                          = "internal"
    subnet_id                     = azurerm_subnet.lastmileuy.id
    private_ip_address_allocation = "Dynamic"
    public_ip_address_id          = azurerm_public_ip.lastmileuy.id
  }

  tags = var.tags
}