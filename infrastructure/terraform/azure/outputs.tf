output "public_ip" {
  description = "IP pública del servidor."
  value       = azurerm_public_ip.main.ip_address
}

output "fqdn" {
  description = "Nombre DNS público (úsalo como SITE_ADDRESS del proxy y como base de la API en la app Android: https://<fqdn>/)."
  value       = azurerm_public_ip.main.fqdn
}

output "ssh_command" {
  description = "Comando para entrar por SSH."
  value       = "ssh ${var.admin_username}@${azurerm_public_ip.main.fqdn}"
}

output "ansible_inventory" {
  description = "Línea para infrastructure/ansible/inventory/qa.ini."
  value       = "medicore-qa ansible_host=${azurerm_public_ip.main.fqdn} ansible_user=${var.admin_username} medicore_site_address=${azurerm_public_ip.main.fqdn}"
}

output "data_disk_lun0_device" {
  description = "Ruta estable del disco de datos dentro del servidor (la usa Ansible)."
  value       = var.data_disk_size_gb > 0 ? "/dev/disk/azure/scsi1/lun0" : ""
}
