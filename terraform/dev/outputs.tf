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
