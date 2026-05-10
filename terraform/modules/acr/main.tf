###############################################################################
# ACR module — Azure Container Registry for PhotoGallery images
#
# Design decisions (see DESIGN_DECISIONS.md D014):
#   * SKU **Basic** (~$5/mo). Cheapest tier; supports admin-user auth and ACR
#     Tasks but no geo-replication. Sufficient for a single-region MVP.
#   * `admin_enabled = false`. Pulls authenticate via AAD/Managed Identity
#     (AcrPull on the ACA UAMI); pushes use AAD via `az acr login` (AcrPush
#     on the developer principal). No long-lived registry credentials live
#     anywhere — including Key Vault — by design.
#   * No private endpoint at this tier (Basic doesn't support it). Network
#     access is public; AAD does the auth heavy lifting.
###############################################################################

resource "azurerm_container_registry" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = var.sku
  admin_enabled       = var.admin_enabled

  tags = var.tags
}
