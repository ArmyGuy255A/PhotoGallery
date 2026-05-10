output "resource_group_name" {
  value = data.azurerm_resource_group.this.name
}

output "storage_account_name" {
  value = module.storage.storage_account_name
}

output "blob_endpoint" {
  value = module.storage.blob_endpoint
}

output "container_name" {
  value = module.storage.container_name
}

output "sql_server_fqdn" {
  value = module.sql.server_fqdn
}

output "sql_database_name" {
  value = module.sql.database_name
}

output "key_vault_name" {
  value = module.keyvault.vault_name
}

output "key_vault_uri" {
  value = module.keyvault.vault_uri
}

output "app_insights_connection_string" {
  value     = module.observability.app_insights_connection_string
  sensitive = true
}

###############################################################################
# Container Apps (API compute)
###############################################################################

output "container_app_name" {
  value = module.compute.container_app_name
}

output "container_app_url" {
  description = "External HTTPS URL of the API container app."
  value       = module.compute.container_app_url
}

output "container_app_uami_name" {
  description = "Name of the API's user-assigned MI. Use verbatim in the SQL CREATE USER ... FROM EXTERNAL PROVIDER post-apply step."
  value       = module.compute.uami_name
}

output "container_app_uami_principal_id" {
  value = module.compute.uami_principal_id
}

###############################################################################
# Container Registry
###############################################################################

output "container_registry_name" {
  description = "ACR name (e.g. acrpgdeva4pi)."
  value       = module.acr.name
}

output "container_registry_login_server" {
  description = "Full ACR login server (e.g. acrpgdeva4pi.azurecr.io). Use in `az acr login` and as the docker push target."
  value       = module.acr.login_server
}
