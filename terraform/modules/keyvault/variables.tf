variable "key_vault_name"      { type = string }
variable "resource_group_name" { type = string }
variable "location"            { type = string }
variable "tenant_id"           { type = string }

variable "dev_principal_object_id" {
  description = "AAD object ID of the developer who runs the app locally."
  type        = string
}

# Wired from the SQL module
variable "sql_connection_string" {
  type      = string
  sensitive = true
}

# Wired from the storage module — used by the app to construct the blob client
variable "storage_account_name"   { type = string }
variable "storage_blob_endpoint"  { type = string }
variable "storage_container_name" { type = string }

variable "tags" {
  type    = map(string)
  default = {}
}
