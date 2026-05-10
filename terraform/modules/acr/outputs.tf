output "id" {
  description = "Resource ID of the container registry. Use as scope for AcrPull/AcrPush role assignments."
  value       = azurerm_container_registry.this.id
}

output "name" {
  description = "Container registry name (e.g. acrpgdeva4pi)."
  value       = azurerm_container_registry.this.name
}

output "login_server" {
  description = "Fully qualified registry login server (e.g. acrpgdeva4pi.azurecr.io). Use in `az acr login` and as the `registry.server` value on the container app."
  value       = azurerm_container_registry.this.login_server
}
