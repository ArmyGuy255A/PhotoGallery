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

###############################################################################
# Custom-domain bindings (optional — created when var.custom_domain_name set).
#
# Apex uses dns-txt-token validation. The caller (terraform/dev) MUST publish
# a TXT record at the zone apex whose value is `apex_validation_token` BEFORE
# this resource's create call completes. Two-step apply:
#   1. terraform apply -target=azurerm_dns_zone.<this>            # zone first
#   2. terraform apply -target=module.staticwebapp.azurerm_static_web_app_custom_domain.apex
#      # ^ creates the resource so .validation_token is populated in state;
#      #   the operation will block until step 3 publishes the TXT.
#   3. In a SEPARATE shell/run: terraform apply                   # publishes
#      the TXT record (which now reads the token from state) and unblocks
#      validation.
#
# In practice the simplest flow is to run a single `terraform apply` after the
# zone exists — azurerm exposes the validation_token early enough that the TXT
# resource (which depends on the custom-domain resource) is created promptly
# and validation succeeds before the SWA resource's create timeout (10 min).
###############################################################################

resource "azurerm_static_web_app_custom_domain" "apex" {
  count             = var.custom_domain_name == "" ? 0 : 1
  static_web_app_id = azurerm_static_web_app.this.id
  domain_name       = var.custom_domain_name
  validation_type   = "dns-txt-token"

  # SWA validation can take a few minutes after the TXT record propagates.
  timeouts {
    create = "30m"
    delete = "30m"
  }
}

resource "azurerm_static_web_app_custom_domain" "www" {
  count             = var.custom_domain_name == "" ? 0 : 1
  static_web_app_id = azurerm_static_web_app.this.id
  domain_name       = "www.${var.custom_domain_name}"
  validation_type   = "cname-delegation"

  timeouts {
    create = "30m"
    delete = "30m"
  }
}
