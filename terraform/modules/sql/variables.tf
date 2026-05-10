variable "server_name"        { type = string }
variable "database_name"      { type = string }
variable "resource_group_name" { type = string }
variable "location"           { type = string }

variable "sku_name" {
  description = "Azure SQL DB SKU. S0 is 10 DTU / 250 GB, ~$15/mo. Bump to S1 if you outgrow."
  type        = string
  default     = "S0"
}

variable "max_size_gb" {
  type    = number
  default = 10
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
