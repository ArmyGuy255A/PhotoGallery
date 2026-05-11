###############################################################################
# Key Vault module — secrets store for PhotoGallery dev
#
# Design decisions (see DESIGN_DECISIONS.md):
#   * Standard SKU. Premium (HSM) is overkill for dev.
#   * RBAC authorization model (not access policies) — modern, role-based,
#     fits managed identities and AAD groups cleanly.
#   * Secret naming: ASP.NET Core Key Vault config provider maps `--` to `:`,
#     so `ConnectionStrings--DefaultConnection` becomes
#     `ConnectionStrings:DefaultConnection`. The canonical secret-name
#     contract is locked — see `Documentation/Runbooks/local-azure-dev.md`
#     ("Key Vault secret contract") and `PhotoGallery/ConfigurationCanonicalAliases.cs`
#     (.NET-side source of truth). Do not deviate from these names.
#   * Soft-delete is on by default and not disablable in current API versions.
#   * Purge protection OFF for dev (so we can wipe and recreate). Turn ON for
#     prod.
###############################################################################

resource "azurerm_key_vault" "this" {
  name                       = var.key_vault_name
  resource_group_name        = var.resource_group_name
  location                   = var.location
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = false
  soft_delete_retention_days = 7

  public_network_access_enabled = true

  tags = var.tags
}

# Dev principal: read secrets from the local app via DefaultAzureCredential
resource "azurerm_role_assignment" "dev_secrets_user" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = var.dev_principal_object_id
}

# Dev principal: write secrets through `terraform apply` and `az keyvault secret set`
resource "azurerm_role_assignment" "dev_secrets_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = var.dev_principal_object_id
}

###############################################################################
# Seed secrets the app needs at boot. Real values are placeholders here —
# rotate via `az keyvault secret set` after `terraform apply`. Terraform won't
# fight you on subsequent applies because we ignore_changes on `value`.
###############################################################################

locals {
  # Canonical secret-name contract (locked by backend dev). KV uses `--` as the
  # `:` separator for the ASP.NET Core Key Vault config provider. See the
  # runbook's "Key Vault secret contract" table and
  # `PhotoGallery/ConfigurationCanonicalAliases.cs`.
  seed_secrets = {
    # Real value composed by Terraform from the SQL module output.
    "ConnectionStrings--DefaultConnection" = var.sql_connection_string

    # Real value (blob primary endpoint). Optional in KV — `Storage:AzureBlob:AccountUrl`
    # also lives in `appsettings.Trial.json`. Seeded here so KV is
    # the single rotatable source if the storage account is ever swapped.
    "Storage--AzureBlob--AccountUrl" = var.storage_blob_endpoint

    # Placeholders — operator fills via `az keyvault secret set` after apply.
    "Authentication--Google--ClientId"                    = "<TO-BE-SET>"
    "Authentication--Google--ClientSecret"                = "<TO-BE-SET>"
    "Authentication--Jwt--Key"                            = "<TO-BE-SET>"
    "Email--AzureCommunicationServices--ConnectionString" = "<TO-BE-SET>"
  }
}

resource "azurerm_key_vault_secret" "seed" {
  for_each     = local.seed_secrets
  name         = each.key
  value        = each.value
  key_vault_id = azurerm_key_vault.this.id

  lifecycle {
    ignore_changes = [value, tags] # let `az keyvault secret set` win after seed
  }

  depends_on = [
    azurerm_role_assignment.dev_secrets_officer,
  ]
}
