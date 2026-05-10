variable "storage_account_name" {
  description = "Globally unique storage account name (3-24 chars, lowercase alphanumeric)."
  type        = string
}

variable "resource_group_name" { type = string }
variable "location" { type = string }

variable "container_name" {
  description = "Blob container holding PhotoGallery photos/derivatives."
  type        = string
  default     = "photogallery"
}

variable "cors_allowed_origins" {
  description = "Origins allowed to fetch SAS-signed blob URLs from the browser."
  type        = list(string)
  default     = ["http://localhost:4200", "https://localhost:4200"]
}

variable "dev_principal_object_id" {
  description = "AAD object ID of the developer (or group) that runs the local app."
  type        = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
