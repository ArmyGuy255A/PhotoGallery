variable "container_app_environment_name" {
  description = "Container Apps Environment name (e.g. cae-photogallery-dev). Ignored when existing_environment_id is supplied."
  type        = string
}

variable "existing_environment_id" {
  description = <<-EOT
    Resource ID of an existing Container Apps Environment to attach to.
    When non-empty, this module does NOT create its own environment — it
    just provisions the container app inside the supplied env. Used by the
    sibling worker compute instantiation to share the API's environment
    (one CAE per region keeps cost down).
  EOT
  type        = string
  default     = ""
}

variable "container_app_name" {
  description = <<-EOT
    Container App name. Also used verbatim as the principal name for the SQL
    `CREATE USER ... FROM EXTERNAL PROVIDER` post-apply step, so keep it
    stable.
  EOT
  type        = string
}

variable "resource_group_name" { type = string }
variable "location" { type = string }

variable "log_analytics_workspace_id" {
  description = "Resource ID of the Log Analytics workspace for ACA env logs."
  type        = string
}

variable "image" {
  description = <<-EOT
    Container image. Default is the Container Apps placeholder image so the
    resource can exist before the real PhotoGallery API image is published to
    GHCR by the CI/CD pipeline. Override later with e.g.
      ghcr.io/armyguy255a/photogallery-backend:<tag>
  EOT
  type        = string
  default     = "mcr.microsoft.com/k8se/quickstart:latest"
}

variable "target_port" {
  description = <<-EOT
    Container port the API listens on. The PhotoGallery .NET API defaults to
    5105 locally, but we standardize on 8080 in Azure (set via the
    ASPNETCORE_URLS env var below). The placeholder image will not actually
    answer on this port — that's fine; ingress comes alive when the real
    image is pushed.
  EOT
  type        = number
  default     = 8080
}

variable "cpu" {
  description = "vCPU per replica. 0.5 is the cheapest valid value on Consumption."
  type        = number
  default     = 0.5
}

variable "memory" {
  description = "Memory per replica. With cpu=0.5, must be 1Gi."
  type        = string
  default     = "1Gi"
}

variable "min_replicas" {
  description = "Min replicas. 0 = scale-to-zero on Consumption (idle cost ≈ $0)."
  type        = number
  default     = 0
}

variable "max_replicas" {
  description = "Max replicas. Cap at 1 for dev to keep cost predictable."
  type        = number
  default     = 1
}

variable "ingress_enabled" {
  description = <<-EOT
    Whether this container app exposes external HTTP ingress.
    - true  → API replica; ingress is wired on `target_port` with HTTPS.
    - false → worker replica; no ingress, runs background workers only.
  EOT
  type        = bool
  default     = true
}

variable "http_scale_concurrent_requests" {
  description = <<-EOT
    KEDA HTTP scaler: spin up a new replica per N concurrent requests.
    Only honoured when `ingress_enabled = true`. Set to 0 to disable the
    HTTP rule (falls back to ACA's default min/max bounds).
  EOT
  type        = number
  default     = 30
}

variable "queue_depth_scale_rule" {
  description = <<-EOT
    DEPRECATED — KEDA's MSSQL scaler runs outside the container and cannot use
    AAD/UAMI auth, so against our AAD-only SQL server it fails with
    "Login failed for user ''" and the worker never scales up. Replaced by
    `cpu_scale_rule` below, which uses ACA's container-level metrics and
    needs no DB credentials.

    Left here for backwards compat and to document why we don't use it.
    Pass `null` to skip (the default).
  EOT
  type = object({
    secret_name      = string
    query            = string
    target_value     = string
    activation_value = string
  })
  default = null
}

variable "cpu_scale_rule" {
  description = <<-EOT
    Optional CPU-based scaler. When set, the container app scales up once
    average CPU across replicas exceeds `utilization` percent. Uses ACA's
    built-in container metrics, so it works regardless of DB auth scheme
    and doesn't need any external credentials.

    Shape:
      {
        utilization = "70"   # scale-up threshold (percent, 1-100)
      }
  EOT
  type = object({
    utilization = string
  })
  default = null
}

variable "container_name" {
  description = <<-EOT
    The container name inside the container app's template.
    Default "api"; override to "worker" for the worker replica so logs and
    ACA metrics are clearly labelled.
  EOT
  type        = string
  default     = "api"
}

variable "key_vault_id" {
  description = "Resource ID of the Key Vault the app reads secrets from."
  type        = string
}

variable "kv_secret_ids" {
  description = <<-EOT
    Map of logical name -> versionless KV secret URI. Each entry becomes:
      * a `secret { key_vault_secret_id = ... identity = "System" }` block
      * an env var the API consumes, mapped via var.kv_env_mapping
    Logical names are lowercased + dashed (KV side uses `--` -> `:`).
  EOT
  type        = map(string)
}

variable "kv_env_mapping" {
  description = <<-EOT
    Map of logical secret name -> ASP.NET Core config key (env var name) the
    container should expose. Use `__` (double underscore) for the `:`
    separator so .NET binds it cleanly without KV's special `--` handling.
    Example: { "sql-conn" = "ConnectionStrings__DefaultConnection" }
  EOT
  type        = map(string)
}

variable "storage_account_id" {
  description = "Resource ID of the Storage Account the API reads/writes blobs against."
  type        = string
}

variable "extra_env" {
  description = "Plain (non-secret) env vars to inject into the API container."
  type        = map(string)
  default     = {}
}

variable "app_insights_connection_string" {
  description = "Application Insights connection string (passed as plain env, not via KV)."
  type        = string
  default     = ""
  sensitive   = true
}

variable "container_registry_server" {
  description = <<-EOT
    Optional ACR login server (e.g. "acrpgdeva4pi.azurecr.io"). When non-empty,
    a `registry` block is added to the container app authenticating via the
    UAMI (which must hold AcrPull on the registry — wired by the caller).
    Leave empty to fall back to anonymous pulls (placeholder image, ghcr.io
    public packages, etc.).
  EOT
  type        = string
  default     = ""
}

variable "tags" {
  type    = map(string)
  default = {}
}
