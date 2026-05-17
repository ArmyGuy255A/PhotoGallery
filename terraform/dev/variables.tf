variable "subscription_id" {
  description = "Target Azure subscription ID. Default is the PhotoGallery dev subscription."
  type        = string
  default     = "4fc243fa-5de2-48cb-9c98-793701d13152"
}

variable "resource_group_name" {
  description = <<-EOT
    Name of the single resource group that holds the entire PhotoGallery dev
    footprint AND the Terraform state storage account. Must start with
    "PhotoGallery" (project convention — see DESIGN_DECISIONS.md D012).
    The RG is created up-front by terraform/bootstrap/bootstrap-state.ps1 and
    adopted here via a data source.
  EOT
  type        = string
  default     = "PhotoGallery-dev"

  validation {
    condition     = startswith(var.resource_group_name, "PhotoGallery")
    error_message = "resource_group_name must start with 'PhotoGallery' (project convention)."
  }
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
  type        = string
  default     = ""
}

variable "cors_allowed_origins" {
  description = "Origins allowed to fetch SAS-signed blob URLs from the browser."
  type        = list(string)
  default = [
    "https://agreeable-tree-043fa290f.7.azurestaticapps.net",
    "http://localhost:4200",
    "https://localhost:4200",
    "http://localhost:4300",
    "https://localhost:4300"
  ]
}

variable "frontend_origin_extra" {
  description = <<-EOT
    Additional origins to add to the API's CORS allowlist beyond the SWA
    default hostname. Useful when running the Angular dev server locally
    against the cloud backend, e.g. ["http://localhost:4200"]. Each entry
    gets injected as Cors__AllowedOrigins__N (N starting at 1; slot 0 is
    reserved for the SWA hostname).
  EOT
  type        = list(string)
  default     = []
}

variable "sql_sku_name" {
  description = "Azure SQL DB SKU. Default Basic (5 DTU, 2 GB) ~$5/mo. Bump to S0 (~$15/mo) when dev data outgrows 2 GB."
  type        = string
  default     = "Basic"
}

variable "sql_max_size_gb" {
  description = "Max DB size in GB. Basic SKU caps at 2."
  type        = number
  default     = 2
}

variable "container_app_image" {
  description = <<-EOT
    Image for the API container. Defaults to the Container Apps placeholder
    image so the resource exists before pg-devops-cicd publishes the real
    PhotoGallery backend image. Override with e.g.
      ghcr.io/armyguy255a/photogallery-backend:<tag>
  EOT
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "github_repository" {
  description = <<-EOT
    GitHub repository in `owner/repo` form. Used as the OIDC subject scope
    for the GitHub Actions federated credential (see DESIGN_DECISIONS.md
    D015). Pushes from refs/heads/main on this repo are trusted to assume
    the SP that holds AcrPush on the ACR.
  EOT
  type        = string
  default     = "ArmyGuy255A/PhotoGallery"

  validation {
    condition     = can(regex("^[^/]+/[^/]+$", var.github_repository))
    error_message = "github_repository must be in 'owner/repo' form."
  }
}

variable "container_app_target_port" {
  description = "Port the API listens on inside the container. Default 8080 — passed to ASPNETCORE_URLS."
  type        = number
  default     = 8080
}

variable "custom_domain_name" {
  description = <<-EOT
    Apex custom domain to bind to the Static Web App, e.g. "appeid.app".
    When set:
      * An Azure DNS zone of the same name is provisioned (the zone's NS
        records must be delegated at the registrar — e.g. GoDaddy).
      * The SWA gets two custom-domain bindings: the apex (dns-txt-token)
        and www.<domain> (cname-delegation). Azure issues and auto-renews
        managed SSL certs (DigiCert) for both — no BYO cert needed.
      * The apex A-alias + www CNAME + validation TXT records are populated
        automatically.
      * The API's CORS allowlist and Frontend__Url are updated to include
        the custom-domain URL alongside the default *.azurestaticapps.net.

    Leave empty to skip custom-domain provisioning entirely (default; the
    SWA remains reachable at its *.azurestaticapps.net hostname).
  EOT
  type        = string
  default     = ""

  validation {
    condition     = var.custom_domain_name == "" || can(regex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", var.custom_domain_name))
    error_message = "custom_domain_name must be an apex domain (e.g. 'appeid.app'), all lower-case, no scheme, no trailing dot."
  }
}
