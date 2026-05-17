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
    "Storage__Provider" = "AzureBlob"

    # The API container app is ingress-only. It NEVER processes photos —
    # ImageSharp + the 0.5 vCPU container starves Kestrel's request thread
    # pool, killing response times (observed in trial). The sibling worker
    # app (min=1) owns 100% of background processing. The API still serves
    # /upload-tickets, /upload-complete, /api/photos/... etc.
    "WorkersEnabled" = "false"

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
    # hostname; slot 1 (when configured) is the custom-domain URL; further
    # slots come from var.frontend_origin_extra (e.g. http://localhost:4200
    # when running the FE locally against the cloud API). Frontend.Url
    # points at the custom domain when set (so OAuth return URLs land on
    # the production hostname) and otherwise falls back to the SWA default.
    "Cors__AllowedOrigins__0" = module.staticwebapp.default_host_url
    "Frontend__Url"           = local.custom_domain_enabled ? "https://${var.custom_domain_name}" : module.staticwebapp.default_host_url
    }, local.custom_domain_enabled ? {
    "Cors__AllowedOrigins__1" = "https://${var.custom_domain_name}"
    "Cors__AllowedOrigins__2" = "https://www.${var.custom_domain_name}"
    } : {}, {
    for idx, origin in var.frontend_origin_extra :
    "Cors__AllowedOrigins__${idx + (local.custom_domain_enabled ? 3 : 1)}" => origin
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

  # min=1 keeps one worker always on so newly-uploaded photos start
  # processing within seconds (no 30-60s cold start). max=3 caps blast
  # radius. Steady-state cost is one ~0.25 vCPU-seconds-billed replica
  # idling between ticks; bulk uploads scale to 3 within ~30s of the CPU
  # crossing the threshold below.
  min_replicas = 1
  max_replicas = 3

  # Bump worker to 1.0 vCPU / 2 GiB. The previous 0.5 vCPU / 1 GiB combo
  # was hitting OOMKilled (exit 137) during bulk uploads — ImageSharp
  # decoded high-quality variants in parallel and blew past 1 GiB. The
  # next supported ACA Consumption pair is 1.0/2Gi, which gives us
  # ~2x the working set without changing pricing tier.
  cpu    = 1.0
  memory = "2Gi"

  # CPU-based autoscaler. KEDA's MSSQL scaler can't authenticate against
  # our AAD-only SQL server (it runs outside the container, so no UAMI
  # access), and was failing with "Login failed for user ''". CPU
  # utilization is a clean proxy here because workers are entirely
  # CPU-bound during image resize. 70% triggers a scale-out before the
  # current replica is saturated.
  cpu_scale_rule = {
    utilization = "70"
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
    "Storage__AzureBlob__AccountUrl"    = module.storage.blob_endpoint
    "Storage__AzureBlob__ContainerName" = module.storage.container_name
    "KeyVault__Uri"                     = module.keyvault.vault_uri
    "WorkersEnabled"                    = "true"
    # Drop parallelism to 2: at 4 we were OOMing on 1Gi, and even with
    # 2Gi headroom keeping parallelism conservative lets us bursty-grow
    # replicas (1→2→3) when CPU rises rather than packing more threads
    # into a single replica.
    "PhotoProcessing__WorkerParallelism"    = "2"
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

  custom_domain_name = var.custom_domain_name

  tags = local.common_tags
}

###############################################################################
# DNS — Azure DNS zone for the custom domain (when configured).
#
# Flow (one-time):
#   1. terraform apply with custom_domain_name set. The zone is created and
#      its nameservers are surfaced via the `dns_zone_nameservers` output.
#   2. At the registrar (GoDaddy), replace the default nameservers with the
#      four returned by Azure. Wait 15 min – 1 hr for propagation.
#   3. Re-run terraform apply. The SWA custom-domain validations succeed
#      and managed SSL certs (DigiCert) are issued automatically.
#
# All future DNS records for appeid.app MUST live in this zone — the
# registrar's DNS UI becomes a no-op once nameservers are delegated.
###############################################################################

locals {
  custom_domain_enabled = var.custom_domain_name != ""
}

resource "azurerm_dns_zone" "custom_domain" {
  count               = local.custom_domain_enabled ? 1 : 0
  name                = var.custom_domain_name
  resource_group_name = data.azurerm_resource_group.this.name
  tags                = local.common_tags
}

# Apex A-alias → SWA. Azure DNS alias records support Static Web Apps as a
# target, which is the trick that lets us bind a SWA to the zone apex
# (GoDaddy and most registrars don't support ALIAS at apex).
resource "azurerm_dns_a_record" "swa_apex" {
  count               = local.custom_domain_enabled ? 1 : 0
  name                = "@"
  zone_name           = azurerm_dns_zone.custom_domain[0].name
  resource_group_name = data.azurerm_resource_group.this.name
  ttl                 = 300
  target_resource_id  = module.staticwebapp.id
}

# www.<domain> CNAME → SWA default hostname. Used for cname-delegation
# validation of the www binding and as the actual answer for www traffic.
resource "azurerm_dns_cname_record" "swa_www" {
  count               = local.custom_domain_enabled ? 1 : 0
  name                = "www"
  zone_name           = azurerm_dns_zone.custom_domain[0].name
  resource_group_name = data.azurerm_resource_group.this.name
  ttl                 = 300
  record              = module.staticwebapp.default_host_name
}

# Apex validation TXT — the SWA module emits the token after the apex
# custom-domain resource starts creation. The TXT must be live before
# Azure's validator polls; the 30m create timeout on the SWA custom-domain
# resource gives Terraform room to schedule and propagate the TXT first.
resource "azurerm_dns_txt_record" "swa_apex_validation" {
  count               = local.custom_domain_enabled ? 1 : 0
  name                = "@"
  zone_name           = azurerm_dns_zone.custom_domain[0].name
  resource_group_name = data.azurerm_resource_group.this.name
  ttl                 = 300

  record {
    value = module.staticwebapp.apex_validation_token
  }
}
