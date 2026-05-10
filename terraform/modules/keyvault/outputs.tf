output "vault_uri" {
  value = azurerm_key_vault.this.vault_uri
}

output "vault_name" {
  value = azurerm_key_vault.this.name
}

output "vault_id" {
  value = azurerm_key_vault.this.id
}

# Versionless secret IDs, used by Container Apps to consume KV references via
# system-assigned managed identity. Versionless = the app always reads the
# latest value when it pulls (matches the runbook's "rotate via az keyvault
# secret set" workflow).
output "secret_versionless_ids" {
  description = "Map of seed-secret name -> versionless KV secret ID."
  value = {
    for k, s in azurerm_key_vault_secret.seed :
    k => s.versionless_id
  }
}
