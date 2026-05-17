output "id" {
  value = azurerm_static_web_app.this.id
}

output "name" {
  value = azurerm_static_web_app.this.name
}

output "default_host_name" {
  description = "Default *.azurestaticapps.net hostname (no scheme)."
  value       = azurerm_static_web_app.this.default_host_name
}

output "default_host_url" {
  description = "Default site URL with https:// scheme."
  value       = "https://${azurerm_static_web_app.this.default_host_name}"
}

output "apex_validation_token" {
  description = "TXT-token to publish at the zone apex when binding a custom domain. Empty when custom_domain_name is unset."
  value       = var.custom_domain_name == "" ? "" : azurerm_static_web_app_custom_domain.apex[0].validation_token
}

output "custom_domain_apex_id" {
  description = "Resource ID of the apex custom-domain binding (empty when unbound)."
  value       = var.custom_domain_name == "" ? "" : azurerm_static_web_app_custom_domain.apex[0].id
}

output "custom_domain_www_id" {
  description = "Resource ID of the www.<domain> custom-domain binding (empty when unbound)."
  value       = var.custom_domain_name == "" ? "" : azurerm_static_web_app_custom_domain.www[0].id
}

output "api_key" {
  description = "Deploy API key used by the GitHub Actions SWA deploy step. Sensitive."
  value       = azurerm_static_web_app.this.api_key
  sensitive   = true
}
