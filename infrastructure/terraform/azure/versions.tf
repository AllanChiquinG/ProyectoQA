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

  # El estado vive en Azure Blob Storage (ver ../bootstrap y backend.hcl.example): cifrado en reposo, con versiones y bloqueo.
  # Configuración parcial: terraform init -backend-config=backend.hcl
  backend "azurerm" {}
}

provider "azurerm" {
  features {}

  # Sin claves de acceso ni secretos en el código: la autenticación sale de "az login" (o de variables ARM_* en el pipeline).
  storage_use_azuread = true
  # La suscripción se indica con ARM_SUBSCRIPTION_ID o var.subscription_id (no se versiona).
  subscription_id = var.subscription_id
}
