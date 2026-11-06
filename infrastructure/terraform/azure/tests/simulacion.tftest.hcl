# Ejecución de Terraform SIN Azure y sin costo: el proveedor es simulado (mock_provider), así que no se conecta a ninguna suscripción ni crea recursos reales.
# Recorre el ciclo completo que pide el proyecto: aprovisionar, modificar y destruir (la destrucción ocurre al terminar la prueba).
# Prueba la lógica del código; NO prueba cuotas, nombres ni disponibilidad reales de Azure.
#   terraform init -backend=false && terraform test -verbose

mock_provider "azurerm" {
  mock_resource "azurerm_subnet" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/medicore-qa-rg/providers/Microsoft.Network/virtualNetworks/medicore-qa-vnet/subnets/app"
    }
  }
  mock_resource "azurerm_network_security_group" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/medicore-qa-rg/providers/Microsoft.Network/networkSecurityGroups/medicore-qa-nsg"
    }
  }
  mock_resource "azurerm_public_ip" {
    defaults = {
      id   = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/medicore-qa-rg/providers/Microsoft.Network/publicIPAddresses/medicore-qa-ip"
      fqdn = "medicore-qa-simulado.eastus.cloudapp.azure.com"
    }
  }
  mock_resource "azurerm_network_interface" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/medicore-qa-rg/providers/Microsoft.Network/networkInterfaces/medicore-qa-nic"
    }
  }
  mock_resource "azurerm_linux_virtual_machine" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/medicore-qa-rg/providers/Microsoft.Compute/virtualMachines/medicore-qa-vm"
    }
  }
  mock_resource "azurerm_managed_disk" {
    defaults = {
      id = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/medicore-qa-rg/providers/Microsoft.Compute/disks/medicore-qa-data"
    }
  }
}

variables {
  subscription_id = "00000000-0000-0000-0000-000000000000"
  ssh_public_key  = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIIgpcGRPAKScOH121At+1+A2atGGYE430Ynj6k+BcmJ7 simulacion" # solo la parte pública; la privada se descartó
  admin_cidrs     = ["203.0.113.10/32"]
}

run "aprovisionar" {
  command = apply

  assert {
    condition     = azurerm_linux_virtual_machine.main.size == "Standard_B2s"
    error_message = "El tamaño por defecto debe ser Standard_B2s."
  }
  assert {
    condition     = azurerm_linux_virtual_machine.main.disable_password_authentication
    error_message = "El servidor no debe aceptar contraseñas."
  }
  assert {
    condition     = length(azurerm_managed_disk.data) == 1 && length(azurerm_dev_test_global_vm_shutdown_schedule.main) == 1
    error_message = "Deben existir el disco de datos y el apagado automático."
  }
  assert {
    condition     = toset([for rule in azurerm_network_security_group.main.security_rule : rule.destination_port_range]) == toset(["22", "80", "443"])
    error_message = "Solo se abren 22, 80 y 443; las bases de datos y el broker no tienen regla de entrada."
  }
  assert {
    condition     = one([for rule in azurerm_network_security_group.main.security_rule : rule.source_address_prefixes if rule.name == "ssh-administracion"]) == toset(["203.0.113.10/32"])
    error_message = "SSH solo se abre a los rangos de administración."
  }
}

run "modificar" {
  command = apply

  variables {
    vm_size               = "Standard_B2ms"
    data_disk_size_gb     = 64
    auto_shutdown_enabled = false
  }

  assert {
    condition     = azurerm_linux_virtual_machine.main.size == "Standard_B2ms"
    error_message = "El cambio de tamaño no se aplicó."
  }
  assert {
    condition     = azurerm_managed_disk.data[0].disk_size_gb == 64
    error_message = "El cambio del disco no se aplicó."
  }
  assert {
    condition     = length(azurerm_dev_test_global_vm_shutdown_schedule.main) == 0
    error_message = "El apagado automático debía retirarse."
  }
}

run "rechaza_ssh_abierto_a_internet" {
  command = plan

  variables {
    admin_cidrs = ["0.0.0.0/0"]
  }

  expect_failures = [var.admin_cidrs]
}
