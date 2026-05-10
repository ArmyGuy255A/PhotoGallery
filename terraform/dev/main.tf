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
#   Log Analytics + App Insights (first 5 GB free)                  $0
#   ----------------------------------------------------------- ----------
#   TOTAL dev footprint, idle                                     ~$6-7/mo
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
  sql_server   = "sql-${local.prefix}-${local.env}-${local.suffix}"
  sql_database = local.prefix
  kv_name      = "kv-${local.short_prefix}-${local.env}-${local.suffix}" # <= 24 chars
  log_name     = "log-${local.prefix}-${local.env}"
  ai_name      = "appi-${local.prefix}-${local.env}"
  cae_name     = "cae-${local.prefix}-${local.env}"
  ca_name      = "ca-${local.prefix}-api-${local.env}"

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
  location            = data.azurerm_resource_group.this.location
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

  sql_connection_string  = module.sql.aad_connection_string
  storage_account_name   = module.storage.storage_account_name
  storage_blob_endpoint  = module.storage.blob_endpoint
  storage_container_name = module.storage.container_name

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

  key_vault_id       = module.keyvault.vault_id
  storage_account_id = module.storage.storage_account_id

  # Wire each KV-backed secret as both an ACA secret and an env var the API
  # consumes. `__` (double underscore) maps to `:` in ASP.NET Core config.
  kv_secret_ids = {
    "sql-conn"             = module.keyvault.secret_versionless_ids["Sql--ConnectionString"]
    "auth-jwt-signingkey"  = module.keyvault.secret_versionless_ids["Auth--Jwt--SigningKey"]
    "auth-google-clientid" = module.keyvault.secret_versionless_ids["Auth--Google--ClientId"]
    "auth-google-secret"   = module.keyvault.secret_versionless_ids["Auth--Google--ClientSecret"]
    "acs-conn"             = module.keyvault.secret_versionless_ids["Acs--ConnectionString"]
  }

  kv_env_mapping = {
    "sql-conn"             = "ConnectionStrings__DefaultConnection"
    "auth-jwt-signingkey"  = "Auth__Jwt__SigningKey"
    "auth-google-clientid" = "Auth__Google__ClientId"
    "auth-google-secret"   = "Auth__Google__ClientSecret"
    "acs-conn"             = "Acs__ConnectionString"
  }

  extra_env = {
    "Storage__Type"                 = "Azure"
    "Storage__Azure__AccountName"   = module.storage.storage_account_name
    "Storage__Azure__BlobEndpoint"  = module.storage.blob_endpoint
    "Storage__Azure__ContainerName" = module.storage.container_name
    "KeyVault__Uri"                 = module.keyvault.vault_uri
  }

  app_insights_connection_string = module.observability.app_insights_connection_string

  tags = local.common_tags
}

