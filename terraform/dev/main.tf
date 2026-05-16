###############################################################################
# PhotoGallery — Dev environment composition
#
# Provisions the data-plane resources the developer hits from a local app:
#   * Resource group
#   * Storage account + photogallery container
#   * Azure SQL Server + photogallery DB (AAD-only)
#   * Key Vault (RBAC) seeded with secrets
#   * Log Analytics + Application Insights (cheap to leave on)
#
# The app itself runs locally (dotnet run / docker-compose for FE) and reaches
# Azure via DefaultAzureCredential resolved from `az login`.
#
# Estimated monthly cost (East US 2, 2026 list prices):
#   Storage Std_LRS hot     ~$0.50 (a few GB) + tx                  ~$1
#   Azure SQL Basic (5 DTU, 2 GB) — flat                           ~$5
#   Key Vault standard      ~$0.03/10k ops                          <$1
#   Container Apps Consumption (scale-to-zero, idle)                ~$0
#     active execution charges only (per request, sub-cent at MVP scale)
#   Azure Container Registry Basic SKU                              ~$5
#   Log Analytics + App Insights (first 5 GB free)                  $0
#   ----------------------------------------------------------- ----------
#   TOTAL dev footprint, idle                                    ~$11-12/mo
###############################################################################

terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.10"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }

  # State lives in the bootstrap-created storage account. See ../bootstrap/.
  # Run `terraform init -backend-config=backend.dev.hcl` on first use.
  backend "azurerm" {
    # All values supplied via -backend-config=backend.dev.hcl
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
  }
  subscription_id = var.subscription_id

  # Use AAD for storage data-plane operations (the storage account has
  # shared_access_key_enabled = false). Required so Terraform's post-create
  # blob-service availability poll can authenticate.
  storage_use_azuread = true
}

provider "azuread" {}

data "azurerm_client_config" "current" {}

###############################################################################
# Naming — globally unique resources get a 4-char random suffix.
###############################################################################

resource "random_string" "suffix" {
  length  = 4
  upper   = false
  special = false
  numeric = true
}

locals {
  prefix       = "photogallery"
  short_prefix = "pg"
  env          = "dev"
  suffix       = random_string.suffix.result

  sa_name      = "st${local.short_prefix}${local.env}${local.suffix}" # 3-24 lowercase alnum
  sql_server   = "sql-${local.prefix}-${local.env}-${local.suffix}-cu"
  sql_database = local.prefix
  kv_name      = "kv-${local.short_prefix}-${local.env}-${local.suffix}" # <= 24 chars
  log_name     = "log-${local.prefix}-${local.env}"
  ai_name      = "appi-${local.prefix}-${local.env}"
  cae_name     = "cae-${local.prefix}-${local.env}"
  ca_name      = "ca-${local.prefix}-api-${local.env}"
  acr_name     = "acr${local.short_prefix}${local.env}${local.suffix}" # 5-50 lowercase alnum, globally unique
  swa_name     = "swa-${local.prefix}-${local.env}"

  common_tags = {
    project     = "PhotoGallery"
    environment = local.env
    managed_by  = "terraform"
    owner       = var.owner_tag
  }
}

###############################################################################
# Resource group
#
# Per DESIGN_DECISIONS.md D012, PhotoGallery uses a SINGLE resource group named
# "PhotoGallery-dev" that holds both the Terraform state storage account AND
# the workload resources. The RG is created up-front by
# terraform/bootstrap/bootstrap-state.ps1 (chicken-and-egg with the state SA),
# so Terraform adopts it via a data source instead of managing it.
###############################################################################

data "azurerm_resource_group" "this" {
  name = var.resource_group_name
}

###############################################################################
# Modules
###############################################################################

module "storage" {
  source = "../modules/storage"

  storage_account_name    = local.sa_name
  resource_group_name     = data.azurerm_resource_group.this.name
  location                = data.azurerm_resource_group.this.location
  container_name          = "photogallery"
  cors_allowed_origins    = var.cors_allowed_origins
  dev_principal_object_id = var.dev_principal_object_id
  tags                    = local.common_tags
}

module "sql" {
  source = "../modules/sql"

  server_name         = local.sql_server
  database_name       = local.sql_database
  resource_group_name = data.azurerm_resource_group.this.name
  # MSDN/Visual Studio Enterprise subs are restricted from SQL provisioning
  # in eastus2/eastus/westus2. centralus typically allows it. Override here
  # only — the other resources stay co-located with the RG.
  location            = "centralus"
  sku_name            = var.sql_sku_name
  max_size_gb         = var.sql_max_size_gb
  aad_admin_login     = var.aad_admin_login
  aad_admin_object_id = var.dev_principal_object_id
  dev_public_ip       = var.dev_public_ip
  tags                = local.common_tags
}

module "keyvault" {
  source = "../modules/keyvault"

  key_vault_name          = local.kv_name
  resource_group_name     = data.azurerm_resource_group.this.name
  location                = data.azurerm_resource_group.this.location
  tenant_id               = data.azurerm_client_config.current.tenant_id
  dev_principal_object_id = var.dev_principal_object_id

  sql_connection_string = module.sql.aad_connection_string
  storage_blob_endpoint = module.storage.blob_endpoint

  tags = local.common_tags
}

module "observability" {
  source = "../modules/observability"

  log_analytics_name  = local.log_name
  app_insights_name   = local.ai_name
  resource_group_name = data.azurerm_resource_group.this.name
  location            = data.azurerm_resource_group.this.location
  tags                = local.common_tags
}

###############################################################################
# Container Registry — Azure Container Registry (Basic SKU, ~$5/mo).
#
# Per DESIGN_DECISIONS.md D014, we pivoted from ghcr.io to ACR so the image
# tier lives in the same RG/sub as the rest of the dev footprint (single
# billing/access boundary) and pulls authenticate via the ACA UAMI without
# managing external registry creds. Pushes from a developer machine use AAD
# via `az acr login` (AcrPush role assigned below).
###############################################################################

module "acr" {
  source = "../modules/acr"

  name                = local.acr_name
  resource_group_name = data.azurerm_resource_group.this.name
  location            = data.azurerm_resource_group.this.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.common_tags
}

# AcrPull for the ACA user-assigned managed identity so the container app can
# pull private images at revision-deploy time. Wired here (not in the compute
# module) to keep cross-module role assignments in the composition root and
# avoid a circular dep between compute and acr modules.
resource "azurerm_role_assignment" "aca_acr_pull" {
  scope                = module.acr.id
  role_definition_name = "AcrPull"
  principal_id         = module.compute.uami_principal_id
}

# AcrPush for the developer principal so `az acr login` + `docker push` works
# locally without enabling the admin user on ACR.
resource "azurerm_role_assignment" "dev_acr_push" {
  scope                = module.acr.id
  role_definition_name = "AcrPush"
  principal_id         = var.dev_principal_object_id
}

###############################################################################
# GitHub Actions OIDC — workload-identity-federated service principal that
# the CI pipeline assumes (no long-lived secrets) to push images to ACR on
# merge-to-main. See DESIGN_DECISIONS.md D015.
###############################################################################

module "github_oidc" {
  source = "../modules/github_oidc"

  display_name      = "photogallery-github-actions-${local.env}"
  github_repository = var.github_repository

  # Only trust pushes on refs/heads/main. Add a `pull-request` subject if/when
  # we need PR pipelines to read from Azure (e.g. preview environments).
  subjects = {
    "main" = "repo:${var.github_repository}:ref:refs/heads/main"
  }
}

# AcrPush so the GitHub Actions SP can publish images to the ACR.
resource "azurerm_role_assignment" "github_actions_acr_push" {
  scope                = module.acr.id
  role_definition_name = "AcrPush"
  principal_id         = module.github_oidc.service_principal_object_id
}

# Contributor on the Container App so the GitHub Actions SP can issue
# `az containerapp update --image ...` and create new revisions when the
# pipeline publishes a new image tag. Scoped to the single container app
# resource — NOT the RG — so the CI identity can't touch storage, KV, SQL,
# or the registry itself beyond AcrPush.
resource "azurerm_role_assignment" "github_actions_aca_contributor" {
  scope                = module.compute.container_app_id
  role_definition_name = "Contributor"
  principal_id         = module.github_oidc.service_principal_object_id
}

###############################################################################
# Compute — Azure Container Apps (Consumption, scale-to-zero) for the API.
# Provisioned now with a placeholder image so the resource exists, ingress is
# wired, and the UAMI can be registered in Azure SQL via the manual T-SQL
# step in the runbook. pg-devops-cicd will publish the real backend image to
# GHCR and flip `container_app_image` (or update the running revision out of
# band — `image` is in `ignore_changes`).
###############################################################################

module "compute" {
  source = "../modules/compute"

  container_app_environment_name = local.cae_name
  container_app_name             = local.ca_name
  resource_group_name            = data.azurerm_resource_group.this.name
  location                       = data.azurerm_resource_group.this.location
  log_analytics_workspace_id     = module.observability.log_analytics_workspace_id

  image       = var.container_app_image
  target_port = var.container_app_target_port

  # Topology: this is the API replica. It runs the workers too (so a quiet
  # MVP doesn't need the sibling worker app spun up), but is pinned to
  # exactly 1 replica. The pin is non-negotiable: SignalR's PhotoProgressHub
  # holds connected WebSocket clients in process memory. Adding a second
  # replica would silently drop progress events for any client connected to
  # the "other" box. The worker-only sibling below has no ingress, so it
  # never accepts WebSocket clients — only the API replica does, and the
  # in-process hub is always reachable by them.
  #
  # If the queue grows past 10 pending rows, the worker-only sibling app
  # scales out (0→N) to chew through the backlog without touching this
  # replica's CPU. Progress events from those worker replicas don't reach
  # SignalR clients — the FE's 5s poll fallback in
  # `upload-progress-aside.component.ts` covers that case so users still see
  # totals tick. Per-photo % bars are real-time only when the API replica
  # itself processed the photo.
  min_replicas = 1
  max_replicas = 1

  # Authenticate ACA pulls against ACR via the UAMI. The AcrPull role
  # assignment is in dev/main.tf (azurerm_role_assignment.aca_acr_pull).
  container_registry_server = module.acr.login_server

  key_vault_id       = module.keyvault.vault_id
  storage_account_id = module.storage.storage_account_id

  # Wire each KV-backed secret as both an ACA secret and an env var the API
  # consumes. ACA secret aliases (the keys below) are lowercase-with-dashes
  # per ACA naming rules; the env-var names use `__` (double underscore)
  # which .NET binds as `:` in config. KV secret names use `--` per the
  # KV config provider's translation rules. All three forms map to the
  # same canonical .NET config path. See the "Key Vault secret contract"
  # table in `Documentation/Runbooks/local-azure-dev.md` and
  # `PhotoGallery/ConfigurationCanonicalAliases.cs`.
  kv_secret_ids = {
    "connectionstrings-defaultconnection" = module.keyvault.secret_versionless_ids["ConnectionStrings--DefaultConnection"]
    "authentication-jwt-key"              = module.keyvault.secret_versionless_ids["Authentication--Jwt--Key"]
    "authentication-google-clientid"      = module.keyvault.secret_versionless_ids["Authentication--Google--ClientId"]
    "authentication-google-clientsecret"  = module.keyvault.secret_versionless_ids["Authentication--Google--ClientSecret"]
    "email-acs-connectionstring"          = module.keyvault.secret_versionless_ids["Email--AzureCommunicationServices--ConnectionString"]
  }

  kv_env_mapping = {
    "connectionstrings-defaultconnection" = "ConnectionStrings__DefaultConnection"
    "authentication-jwt-key"              = "Authentication__Jwt__Key"
    "authentication-google-clientid"      = "Authentication__Google__ClientId"
    "authentication-google-clientsecret"  = "Authentication__Google__ClientSecret"
    "email-acs-connectionstring"          = "Email__AzureCommunicationServices__ConnectionString"
  }

  extra_env = merge({
    # Provider selectors
    "Storage__Provider"  = "AzureBlob"
    "Database__Provider" = "SqlServer"

    # Workers run on the API replica too — this is the steady-state
    # MVP path before the worker app spins up. Keep parallelism low so
    # image processing doesn't starve the request thread pool on the
    # 0.5 vCPU box. Admin can hot-reload these via /admin/settings.
    "WorkersEnabled"                        = "true"
    "PhotoProcessing__WorkerParallelism"    = "2"
    "PhotoProcessing__LeaseBatchMultiplier" = "2"

    # Storage (non-secret config) — AccountUrl is also seeded in KV for
    # rotatability, but resolving it from a plain env var is the default
    # path. KV wins only if the same key is bound through the KV provider.
    "Storage__AzureBlob__AccountUrl"    = module.storage.blob_endpoint
    "Storage__AzureBlob__ContainerName" = module.storage.container_name

    # Key Vault URI — bootstraps the KV config provider in Program.cs.
    "KeyVault__Uri" = module.keyvault.vault_uri

    # CORS allowlist — origin(s) the API will permit cross-origin requests
    # from. Indexed (`__0`, `__1`, ...) so .NET binds them into the
    # `Cors:AllowedOrigins` List<string>. Slot 0 is the SWA default
    # hostname; slots 1..N come from var.frontend_origin_extra (e.g.
    # http://localhost:4200 when running the FE locally against the cloud
    # API). Frontend.Url is also pointed at the SWA so OAuth return URLs
    # land back on the deployed SPA.
    "Cors__AllowedOrigins__0" = module.staticwebapp.default_host_url
    "Frontend__Url"           = module.staticwebapp.default_host_url
    }, {
    for idx, origin in var.frontend_origin_extra :
    "Cors__AllowedOrigins__${idx + 1}" => origin
  })

  app_insights_connection_string = module.observability.app_insights_connection_string

  tags = local.common_tags
}

###############################################################################
# Compute (worker) — sibling Container App that runs the same image with no
# ingress. Scales 0→N on queue depth via a KEDA MSSQL custom scaler so we
# only pay for image-processing CPU when there's actual work to do.
#
# Topology rationale:
#   - API replica (above) is pinned to exactly 1 because SignalR's hub holds
#     connected WebSocket clients in process memory and we deliberately do
#     not run Azure SignalR Service. Adding a 2nd API replica would silently
#     drop events for clients connected to the "other" replica.
#   - Worker replicas never accept HTTP, so SignalR clients never connect
#     here. The hub on a worker replica is unused; progress events from
#     workers here don't reach FE clients. The FE 5s poll fallback in
#     upload-progress-aside.component.ts masks this (the per-album summary
#     stays correct, per-photo % only animates for photos processed on the
#     API replica). Acceptable tradeoff to avoid Azure SignalR.
#
# Scaling:
#   - min_replicas = 0 → scale-to-zero when queue is empty (no cost when
#     idle, just like before this split).
#   - max_replicas = 3 → cap blast radius on a runaway queue.
#   - activationValue = 10 → don't wake from 0 until at least 10 pending
#     rows. Matches the user-defined "more than 10 photos" threshold.
#   - targetValue   = 10 → 1 worker replica per 10 pending rows. So 100
#     pending rows → 3 replicas (capped). Tunable.
#
# After apply, the worker UAMI must be registered as a SQL DB user via the
# same T-SQL step we use for the API UAMI (see
# Documentation/Runbooks/local-azure-dev.md):
#   CREATE USER [<worker-uami-name>] FROM EXTERNAL PROVIDER;
#   ALTER ROLE db_datareader  ADD MEMBER [<worker-uami-name>];
#   ALTER ROLE db_datawriter  ADD MEMBER [<worker-uami-name>];
#   ALTER ROLE db_ddladmin    ADD MEMBER [<worker-uami-name>];  -- migrations on startup
# The UAMI name is exposed via module.compute_worker.uami_name.
###############################################################################

module "compute_worker" {
  source = "../modules/compute"

  # Re-use the API's Container Apps Environment — one CAE per region is
  # cheaper and lets both apps share the Log Analytics workspace.
  container_app_environment_name = local.cae_name
  existing_environment_id        = module.compute.container_app_environment_id

  container_app_name         = "ca-${local.prefix}-worker-${local.env}"
  resource_group_name        = data.azurerm_resource_group.this.name
  location                   = data.azurerm_resource_group.this.location
  log_analytics_workspace_id = module.observability.log_analytics_workspace_id

  image          = var.container_app_image
  target_port    = var.container_app_target_port
  container_name = "worker"

  ingress_enabled                = false
  http_scale_concurrent_requests = 0
  min_replicas                   = 0
  max_replicas                   = 3

  queue_depth_scale_rule = {
    secret_name      = "connectionstrings-defaultconnection"
    query            = "SELECT COUNT(*) FROM ProcessingQueueItems WHERE Status IN (0, 1)"
    target_value     = "10"
    activation_value = "10"
  }

  container_registry_server = module.acr.login_server

  key_vault_id       = module.keyvault.vault_id
  storage_account_id = module.storage.storage_account_id

  # Same secret set as the API replica. The worker needs the SQL conn
  # string (queue + photos) and storage credentials; the other secrets
  # are harmless extras since the worker code paths don't read them.
  kv_secret_ids = {
    "connectionstrings-defaultconnection" = module.keyvault.secret_versionless_ids["ConnectionStrings--DefaultConnection"]
  }

  kv_env_mapping = {
    "connectionstrings-defaultconnection" = "ConnectionStrings__DefaultConnection"
  }

  extra_env = {
    "Storage__Provider"                 = "AzureBlob"
    "Database__Provider"                = "SqlServer"
    "Storage__AzureBlob__AccountUrl"    = module.storage.blob_endpoint
    "Storage__AzureBlob__ContainerName" = module.storage.container_name
    "KeyVault__Uri"                     = module.keyvault.vault_uri
    "WorkersEnabled"                    = "true"
    # Workers here have a whole 0.5 vCPU to themselves (no request thread
    # pool to share with) so we can crank parallelism higher than the API
    # replica. Admin can hot-reload via /admin/settings.
    "PhotoProcessing__WorkerParallelism"    = "4"
    "PhotoProcessing__LeaseBatchMultiplier" = "4"
  }

  app_insights_connection_string = module.observability.app_insights_connection_string

  tags = local.common_tags
}

# AcrPull for the worker UAMI so it can pull the same image.
resource "azurerm_role_assignment" "worker_acr_pull" {
  scope                = module.acr.id
  role_definition_name = "AcrPull"
  principal_id         = module.compute_worker.uami_principal_id
}

# Contributor on the worker container app so the CI pipeline can update
# its image alongside the API's.
resource "azurerm_role_assignment" "github_actions_worker_contributor" {
  scope                = module.compute_worker.container_app_id
  role_definition_name = "Contributor"
  principal_id         = module.github_oidc.service_principal_object_id
}


###############################################################################
# Static Web Apps — Free-tier hosting for the Angular frontend.
#
# See DESIGN_DECISIONS.md D015. The deploy GitHub Action (owned by the FE
# dev) reads the api_key output to push built artifacts. SWA exposes the
# backend FQDN as BACKEND_API_URL so the SPA's staticwebapp.config.json can
# optionally proxy /api/* to the ACA container app.
###############################################################################

module "staticwebapp" {
  source = "../modules/staticwebapp"

  name                = local.swa_name
  resource_group_name = data.azurerm_resource_group.this.name
  # SWA Free is region-restricted. eastus2 is in the allow-list and matches
  # the rest of the dev footprint's region.
  location = "eastus2"
  sku_tier = "Free"
  sku_size = "Free"
  # Intentionally NOT passing backend_api_url here — the API's CORS list
  # already depends on the SWA hostname (compute -> staticwebapp), and
  # cross-wiring SWA -> compute via BACKEND_API_URL would create a cycle.
  # The FE GH Actions deploy step sets BACKEND_API_URL out of band after
  # both resources exist (`az staticwebapp appsettings set ...`).

  tags = local.common_tags
}
