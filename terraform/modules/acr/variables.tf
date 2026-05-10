variable "name" {
  description = <<-EOT
    Globally-unique ACR name. Lowercase alphanumeric only, 5-50 chars.
    Convention: acr<short_prefix><env><suffix>, e.g. acrpgdeva4pi.
  EOT
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9]{5,50}$", var.name))
    error_message = "ACR name must be 5-50 lowercase alphanumeric characters."
  }
}

variable "resource_group_name" {
  type = string
}

variable "location" {
  type = string
}

variable "sku" {
  description = <<-EOT
    ACR SKU. Defaults to Basic (~$5/mo) — cheapest tier; supports admin-user
    auth and ACR Tasks but no geo-replication. Bump to Standard/Premium only
    when geo-replication, private link, or higher storage quota is needed.
  EOT
  type        = string
  default     = "Basic"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku)
    error_message = "sku must be Basic, Standard, or Premium."
  }
}

variable "admin_enabled" {
  description = <<-EOT
    Whether to enable the built-in admin user (username/password). We default
    OFF — pulls go through AAD/Managed Identity (AcrPull role) and pushes
    use AAD via `az acr login` (AcrPush role). Avoids long-lived credentials.
  EOT
  type        = bool
  default     = false
}

variable "tags" {
  type    = map(string)
  default = {}
}
