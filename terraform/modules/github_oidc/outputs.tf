output "client_id" {
  description = "Application (client) ID of the AAD app. Set as the AZURE_CLIENT_ID GitHub repo variable/secret used by azure/login@v2."
  value       = azuread_application.this.client_id
}

output "application_object_id" {
  description = "Object ID of the AAD application registration."
  value       = azuread_application.this.object_id
}

output "service_principal_object_id" {
  description = "Object ID of the service principal. Use as `principal_id` for azurerm_role_assignment scopes (AcrPush, AcrPull, etc.)."
  value       = azuread_service_principal.this.object_id
}
