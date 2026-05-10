variable "server_name" { type = string }
variable "database_name" { type = string }
variable "resource_group_name" { type = string }
variable "location" { type = string }

variable "sku_name" {
  description = <<-EOT
    Azure SQL DB SKU. Default Basic = 5 DTU, 2 GB cap, ~$5/mo flat — the
    cheapest tier that still supports AAD-only auth and EF Core migrations.
    Bump to S0 (~$15/mo, 250 GB) when dev data outgrows 2 GB, or to
    GP_S_Gen5_1 (serverless w/ auto-pause) if usage becomes very bursty.
  EOT
  type        = string
  default     = "Basic"
}

variable "max_size_gb" {
  description = "Max DB size in GB. Basic SKU caps at 2."
  type        = number
  default     = 2
}

variable "aad_admin_login" {
  description = "UPN/display name shown as AAD admin (e.g. dev@example.com)."
  type        = string
}

variable "aad_admin_object_id" {
  description = "AAD object ID of the user/group that should be SQL admin."
  type        = string
}

variable "dev_public_ip" {
  description = "Dev laptop's public IPv4. Blank = skip the firewall rule."
  type        = string
  default     = ""
}

variable "tags" {
  type    = map(string)
  default = {}
}
