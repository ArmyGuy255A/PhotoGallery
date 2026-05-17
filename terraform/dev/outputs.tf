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

output "container_app_id" {
  description = "Resource ID of the API Container App. Used for least-privilege role assignments and for the CI deploy step to target with `az containerapp update`."
  value       = module.compute.container_app_id
}

###############################################################################
# Container Apps (worker compute) — sibling app, scales 0→N on queue depth.
###############################################################################

output "worker_container_app_name" {
  value = module.compute_worker.container_app_name
}

output "worker_container_app_id" {
  description = "Resource ID of the worker Container App. Used by the CI deploy step to keep the worker's image tag in lockstep with the API."
  value       = module.compute_worker.container_app_id
}

output "worker_container_app_uami_name" {
  description = "Name of the worker's user-assigned MI. terraform/scripts/Register-SqlPrincipals.ps1 grants this principal db_datareader/db_datawriter/db_ddladmin on the photogallery DB."
  value       = module.compute_worker.uami_name
}

# Alias used by Apply.ps1 to discover the DB name without depending on the
# slightly older `sql_database_name` output (which already exists above).
output "sql_database" {
  value = module.sql.database_name
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

###############################################################################
# Static Web App (Angular frontend hosting)
###############################################################################

output "static_web_app_name" {
  description = "Static Web App resource name."
  value       = module.staticwebapp.name
}

output "static_web_app_default_host_name" {
  description = "Default *.azurestaticapps.net hostname for the SWA (no scheme)."
  value       = module.staticwebapp.default_host_name
}

output "static_web_app_url" {
  description = "Full https:// URL where the deployed SPA is served."
  value       = module.staticwebapp.default_host_url
}

output "static_web_app_api_key" {
  description = "Deploy API key for the SWA — used by the FE GitHub Actions workflow."
  value       = module.staticwebapp.api_key
  sensitive   = true
}

###############################################################################
# Custom domain (Azure DNS + SWA bindings). Empty when var.custom_domain_name
# is unset.
###############################################################################

output "custom_domain_name" {
  description = "Configured apex custom domain (empty when unbound)."
  value       = var.custom_domain_name
}

output "custom_domain_url" {
  description = "Full https:// URL for the apex custom domain (empty when unbound)."
  value       = local.custom_domain_enabled ? "https://${var.custom_domain_name}" : ""
}

output "dns_zone_nameservers" {
  description = "Azure-assigned nameservers for the custom-domain DNS zone. Paste these into the registrar (GoDaddy) to delegate DNS. Empty when no custom domain configured."
  value       = local.custom_domain_enabled ? azurerm_dns_zone.custom_domain[0].name_servers : []
}

###############################################################################
# GitHub Actions OIDC — values to set as repo variables/secrets so the CI
# pipeline can authenticate to Azure with no long-lived credentials.
#
# After `terraform apply`, surface and set:
#   gh variable set AZURE_CLIENT_ID       --body "$(terraform output -raw github_actions_client_id)"
#   gh variable set AZURE_TENANT_ID       --body "$(terraform output -raw tenant_id)"
#   gh variable set AZURE_SUBSCRIPTION_ID --body "$(terraform output -raw subscription_id)"
#   gh variable set ACR_LOGIN_SERVER      --body "$(terraform output -raw container_registry_login_server)"
#   gh variable set ACA_BACKEND_NAME      --body "$(terraform output -raw container_app_name)"
#   gh variable set ACA_RESOURCE_GROUP    --body "$(terraform output -raw resource_group_name)"
###############################################################################

output "github_actions_client_id" {
  description = "Client ID of the GitHub Actions OIDC AAD app. Set as the AZURE_CLIENT_ID GitHub repo variable."
  value       = module.github_oidc.client_id
}

output "tenant_id" {
  description = "AAD tenant ID. Set as the AZURE_TENANT_ID GitHub repo variable."
  value       = data.azurerm_client_config.current.tenant_id
}

output "subscription_id" {
  description = "Azure subscription ID. Set as the AZURE_SUBSCRIPTION_ID GitHub repo variable."
  value       = data.azurerm_client_config.current.subscription_id
}
