###############################################################################
# Static Web Apps module — Azure-hosted Angular frontend.
#
# Design notes (see DESIGN_DECISIONS.md D015):
#   * Free tier — $0/mo, 100 GB bandwidth, free SSL, global CDN, custom
#     domains supported. Plenty for dev/MVP.
#   * Source build/deploy lives in GitHub Actions (FE dev owns that workflow).
#     This module only provisions the resource shell and exposes the deploy
#     api_key for the workflow's SWA deploy step.
#   * app_settings carries non-secret runtime config (e.g. BACKEND_API_URL).
#     The FE dev can reference this in staticwebapp.config.json route rewrites
#     or read it at build time.
###############################################################################

resource "azurerm_static_web_app" "this" {
  name                = var.name
  resource_group_name = var.resource_group_name
  location            = var.location

  sku_tier = var.sku_tier
  sku_size = var.sku_size

  # Only set app_settings when backend_api_url is supplied; otherwise leave
  # the map empty so a downstream cycle (SWA -> compute -> SWA via CORS)
  # doesn't get re-introduced.
  app_settings = var.backend_api_url == "" ? {} : {
    BACKEND_API_URL = var.backend_api_url
  }

  tags = var.tags
}
