###############################################################################
# Storage module — Azure Storage Account + Blob container for PhotoGallery
#
# Design decisions (see Documentation/Architecture/DESIGN_DECISIONS.md):
#   * Standard_LRS, Hot access tier (dev volume, no GRS needed).
#   * Public blob anonymous access DISABLED. All reads via user-delegation SAS
#     issued by the app using DefaultAzureCredential.
#   * Soft delete: 7 days for blobs and containers (cheap insurance for dev).
#   * CORS: allow http://localhost:4200 (Angular dev server) so the FE can
#     follow SAS URLs directly without proxying through the API.
#   * RBAC over keys: dev principal gets "Storage Blob Data Contributor" +
#     "Storage Blob Delegator" so the local app can both R/W blobs and mint
#     user-delegation SAS tokens.
###############################################################################

resource "azurerm_storage_account" "this" {
  name                     = var.storage_account_name
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  account_kind             = "StorageV2"
  access_tier              = "Hot"

  min_tls_version                 = "TLS1_2"
  public_network_access_enabled   = true # dev: laptop reaches it directly
  allow_nested_items_to_be_public = false
  shared_access_key_enabled       = false # force AAD; no account-key SAS

  blob_properties {
    versioning_enabled = false

    delete_retention_policy {
      days = 7
    }

    container_delete_retention_policy {
      days = 7
    }

    cors_rule {
      allowed_origins    = var.cors_allowed_origins
      # PUT + POST are required for the direct-to-blob SAS upload flow the
      # SPA uses (PhotoService.uploadPhoto → POST /upload-tickets then
      # browser PUTs the file straight to blob storage). The narrower
      # GET/HEAD/OPTIONS set worked for the legacy multipart path that
      # routed everything through the API; SAS direct upload broke under
      # those CORS rules with ERR_FAILED on the preflight.
      allowed_methods    = ["GET", "HEAD", "OPTIONS", "PUT", "POST"]
      allowed_headers    = [
        "x-ms-blob-type",
        "x-ms-meta-*",
        "x-ms-version",
        "content-type",
        "content-length",
        "if-match",
        "if-none-match",
        "authorization",
        "accept",
        "origin"
      ]
      exposed_headers    = ["*"]
      max_age_in_seconds = 3600
    }
  }

  tags = var.tags
}

resource "azurerm_storage_container" "photos" {
  name                  = var.container_name
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}

# Dev principal: read/write blob data
resource "azurerm_role_assignment" "dev_blob_contributor" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = var.dev_principal_object_id
}

# Dev principal: ability to issue user-delegation SAS via the local app
resource "azurerm_role_assignment" "dev_blob_delegator" {
  scope                = azurerm_storage_account.this.id
  role_definition_name = "Storage Blob Delegator"
  principal_id         = var.dev_principal_object_id
}
