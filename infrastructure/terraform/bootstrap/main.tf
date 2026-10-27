# Bootstrap del ESTADO REMOTO de Terraform. Se ejecuta UNA vez, antes de ../azure, y crea la cuenta de almacenamiento donde vivirá el estado.
#
# Protección del estado (contiene identificadores y atributos de los recursos):
#   - Blob privado, solo HTTPS y TLS 1.2; sin acceso anónimo.
#   - Sin claves de acceso (shared_access_key_enabled = false): solo Microsoft Entra ID con el rol "Storage Blob Data Contributor".
#   - Versionado y retención de borrado de 30 días: se puede recuperar un estado anterior o borrado.
#   - Bloqueo (lease) del blob durante cada operación: Terraform impide dos aplicaciones simultáneas sobre el mismo estado.
#   - Candado de recurso CanNotDelete sobre la cuenta.
# Este módulo usa estado LOCAL (terraform.tfstate en esta carpeta, ignorado por Git): es pequeño, no contiene secretos y se puede reconstruir importando
# el grupo de recursos y la cuenta.

terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }
}

provider "azurerm" {
  features {}

  storage_use_azuread = true
  subscription_id     = var.subscription_id
}

variable "subscription_id" {
  description = "Id de la suscripción de Azure (o ARM_SUBSCRIPTION_ID)."
  type        = string
  default     = null
}

variable "location" {
  description = "Región de la cuenta de almacenamiento del estado."
  type        = string
  default     = "eastus"
}

variable "project" {
  type    = string
  default = "medicore"
}

data "azurerm_client_config" "current" {}

resource "random_string" "suffix" {
  length  = 5
  upper   = false
  special = false
}

resource "azurerm_resource_group" "state" {
  name     = "${var.project}-tfstate-rg"
  location = var.location
  tags     = { proyecto = var.project, proposito = "estado-de-terraform" }
}

resource "azurerm_storage_account" "state" {
  name                     = "${var.project}tfstate${random_string.suffix.result}"
  resource_group_name      = azurerm_resource_group.state.name
  location                 = azurerm_resource_group.state.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"

  min_tls_version                 = "TLS1_2"
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 30
    }

    container_delete_retention_policy {
      days = 30
    }
  }

  tags = { proyecto = var.project, proposito = "estado-de-terraform" }
}

resource "azurerm_storage_container" "state" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.state.id
  container_access_type = "private"
}

# Quien ejecuta el bootstrap puede leer y escribir el estado (otros colaboradores necesitan este mismo rol).
resource "azurerm_role_assignment" "state_contributor" {
  scope                = azurerm_storage_account.state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_management_lock" "state" {
  name       = "no-borrar-estado"
  scope      = azurerm_storage_account.state.id
  lock_level = "CanNotDelete"
  notes      = "Contiene el estado de Terraform del ambiente QA."
}

output "backend_config" {
  description = "Contenido para infrastructure/terraform/azure/backend.hcl"
  value       = <<-EOT
    resource_group_name  = "${azurerm_resource_group.state.name}"
    storage_account_name = "${azurerm_storage_account.state.name}"
    container_name       = "${azurerm_storage_container.state.name}"
    key                  = "qa.terraform.tfstate"
    use_azuread_auth     = true
  EOT
}
