variable "subscription_id" {
  description = "Id de la suscripción de Azure. También se puede dar con la variable de entorno ARM_SUBSCRIPTION_ID."
  type        = string
  default     = null
}

variable "project" {
  description = "Prefijo de los nombres de los recursos."
  type        = string
  default     = "medicore"
}

variable "environment" {
  description = "Ambiente (qa)."
  type        = string
  default     = "qa"
}

variable "location" {
  description = "Región de Azure. Cámbiala por la más cercana o la que permita tu suscripción."
  type        = string
  default     = "eastus"
}

variable "vm_size" {
  description = "Tamaño de la máquina virtual. 2 vCPU y 4 GiB bastan para QA (PostgreSQL, Redis, RabbitMQ, API, web, worker y proxy)."
  type        = string
  default     = "Standard_B2s"
}

variable "admin_username" {
  description = "Usuario administrador del servidor (solo acceso por clave SSH)."
  type        = string
  default     = "azureuser"
}

variable "ssh_public_key" {
  description = "Clave PÚBLICA SSH del administrador (la privada nunca va al repositorio ni a Terraform)."
  type        = string
}

variable "admin_cidrs" {
  description = "Rangos de IP autorizados para SSH (por ejemplo, [\"203.0.113.10/32\"]). Nunca 0.0.0.0/0."
  type        = list(string)

  validation {
    condition     = length(var.admin_cidrs) > 0 && !contains(var.admin_cidrs, "0.0.0.0/0") && !contains(var.admin_cidrs, "*")
    error_message = "admin_cidrs debe tener al menos un rango y no puede abrir SSH a todo Internet."
  }
}

variable "os_disk_size_gb" {
  description = "Tamaño del disco del sistema operativo."
  type        = number
  default     = 64
}

variable "data_disk_size_gb" {
  description = "Disco de datos separado para /var/lib/docker (volúmenes de PostgreSQL, Redis y RabbitMQ). 0 = sin disco de datos."
  type        = number
  default     = 32
}

variable "auto_shutdown_enabled" {
  description = "Apaga la máquina todos los días para ahorrar costo (se vuelve a encender a mano o con az vm start)."
  type        = bool
  default     = true
}

variable "auto_shutdown_time" {
  description = "Hora de apagado diario en formato HHmm (UTC)."
  type        = string
  default     = "0400"
}

variable "tags" {
  description = "Etiquetas adicionales."
  type        = map(string)
  default     = {}
}
