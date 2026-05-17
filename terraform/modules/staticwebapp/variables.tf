variable "name" {
  description = "Static Web App name (e.g. swa-photogallery-dev)."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group hosting the SWA. Per D012, this is PhotoGallery-dev."
  type        = string
}

variable "location" {
  description = <<-EOT
    Azure region. SWA Free is only available in a small set of regions:
    eastus2, centralus, westus2, westeurope, eastasia. Default eastus2 to
    co-locate with the rest of the PhotoGallery-dev footprint.
  EOT
  type        = string
  default     = "eastus2"

  validation {
    condition     = contains(["eastus2", "centralus", "westus2", "westeurope", "eastasia"], var.location)
    error_message = "Static Web Apps Free is only available in eastus2, centralus, westus2, westeurope, or eastasia."
  }
}

variable "sku_tier" {
  description = "SKU tier — Free for dev, Standard later if custom domains / managed functions are needed."
  type        = string
  default     = "Free"
}

variable "sku_size" {
  description = "SKU size — matches sku_tier for the Free/Standard parity."
  type        = string
  default     = "Free"
}

variable "backend_api_url" {
  description = <<-EOT
    Full HTTPS URL of the backend API container app, e.g.
    https://ca-photogallery-api-dev.<region>.azurecontainerapps.io
    Exposed to the SWA build/runtime as the BACKEND_API_URL app setting so
    the FE dev's staticwebapp.config.json can route /api/* there if desired.

    Optional — leaving this empty omits the app_setting entirely (avoids a
    Terraform dependency cycle: SWA -> compute -> SWA when the API's CORS
    list also references the SWA hostname). The FE deploy GitHub Action can
    set BACKEND_API_URL out of band via `az staticwebapp appsettings set`
    once both resources are provisioned.
  EOT
  type        = string
  default     = ""
}

variable "custom_domain_name" {
  description = <<-EOT
    Apex custom domain to bind to the SWA, e.g. "appeid.app". When set, the
    module provisions two azurerm_static_web_app_custom_domain resources:
      * the apex (dns-txt-token validation — caller wires the TXT record)
      * www.<domain> (cname-delegation validation — caller wires the CNAME)
    Leave empty to skip custom-domain binding entirely.

    NOTE: Free SKU supports custom domains and Azure-managed SSL (DigiCert,
    auto-renewed). No BYO cert needed.
  EOT
  type        = string
  default     = ""
}

variable "tags" {
  type    = map(string)
  default = {}
}
