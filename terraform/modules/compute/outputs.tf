output "container_app_name" {
  value = azurerm_container_app.this.name
}

output "container_app_fqdn" {
  description = "External ingress FQDN of the API. Empty string when ingress is disabled (worker replica)."
  value       = var.ingress_enabled ? azurerm_container_app.this.ingress[0].fqdn : ""
}

output "container_app_url" {
  value = var.ingress_enabled ? "https://${azurerm_container_app.this.ingress[0].fqdn}" : ""
}

output "uami_name" {
  description = "User-assigned managed identity name. Use this verbatim in the SQL CREATE USER ... FROM EXTERNAL PROVIDER post-apply step."
  value       = azurerm_user_assigned_identity.aca.name
}

output "uami_principal_id" {
  value = azurerm_user_assigned_identity.aca.principal_id
}

output "uami_client_id" {
  value = azurerm_user_assigned_identity.aca.client_id
}

output "uami_id" {
  value = azurerm_user_assigned_identity.aca.id
}

output "container_app_id" {
  description = "Resource ID of the Container App. Use as scope for role assignments (e.g. AcrPush, Contributor for CI deploys)."
  value       = azurerm_container_app.this.id
}

output "container_app_environment_id" {
  value = local.env_id
}
