variable "subscription_id" {
  description = "Target Azure subscription ID."
  type        = string
}

variable "location" {
  description = "Azure region for the dev footprint."
  type        = string
  default     = "eastus2"
}

variable "owner_tag" {
  description = "Free-form owner tag (your name/email) for cost tracking."
  type        = string
  default     = "photogallery-dev"
}

variable "dev_principal_object_id" {
  description = <<-EOT
    AAD object ID of the developer (or AAD group) running the local app.
    Get with: az ad signed-in-user show --query id -o tsv
  EOT
  type        = string
}

variable "aad_admin_login" {
  description = "AAD admin display name for SQL Server (UPN works)."
  type        = string
}

variable "dev_public_ip" {
  description = <<-EOT
    Dev laptop's public IPv4. Used for the SQL Server firewall rule.
    Find with: curl -s https://api.ipify.org
    Set to "" to skip (e.g. if you're on a static/VPN IP added separately).
  EOT
  type    = string
  default = ""
}

variable "cors_allowed_origins" {
  description = "Origins allowed to fetch SAS-signed blob URLs from the browser."
  type        = list(string)
  default     = ["http://localhost:4200", "https://localhost:4200"]
}

variable "sql_sku_name" {
  type    = string
  default = "S0" # 10 DTU, ~$15/mo
}

variable "sql_max_size_gb" {
  type    = number
  default = 10
}
