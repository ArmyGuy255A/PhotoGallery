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

output "api_key" {
  description = "Deploy API key used by the GitHub Actions SWA deploy step. Sensitive."
  value       = azurerm_static_web_app.this.api_key
  sensitive   = true
}
