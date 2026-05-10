###############################################################################
# Compute module — Azure Container Apps for the PhotoGallery API
#
# Design decisions (see DESIGN_DECISIONS.md D013):
#   * Azure Container Apps, **Consumption** workload profile, scale-to-zero
#     (min=0, max=1), 0.5 vCPU / 1 GiB. Idle cost ≈ $0; pay only for
#     per-request execution time. Materially cheaper than App Service B1
#     ($13/mo flat) for a mostly-idle MVP. Cold-start ~1-3s after idle.
#   * **Placeholder image** (mcr.microsoft.com/k8se/quickstart:latest) so the
#     resource can be provisioned before pg-devops-cicd publishes the real
#     PhotoGallery backend image to GHCR. Real image overrides via
#     `terraform.tfvars` later.
#   * **User-assigned managed identity** (UAMI). Chosen over system-assigned
#     specifically to break the chicken-and-egg of "container app needs the
#     KV role at revision-deploy time, but role assignment needs the
#     principal_id which is only known after create." With UAMI we:
#       1. Create the UAMI (principal_id known immediately)
#       2. Assign KV / Storage roles to the UAMI
#       3. `time_sleep` 60s for AAD role propagation
#       4. Create the container app, attach the UAMI, declare KV secrets
#          with `identity = <uami id>` — secrets resolve cleanly on first
#          revision deploy.
#     The UAMI is also the principal we register in Azure SQL via the manual
#     `CREATE USER [<uami-name>] FROM EXTERNAL PROVIDER` post-apply step.
#   * **Secrets via KV references**, not literal env vars. Each KV secret is
#     a `secret { key_vault_secret_id = ... identity = <uami id> }` block;
#     env vars then bind via `secret_name`.
#   * **No registry credential block** — the placeholder image lives on MCR
#     (anonymous pull); the real PhotoGallery image lives on ghcr.io as a
#     public package. Add ACR Basic ($5/mo) only when private images become a
#     requirement.
###############################################################################

resource "azurerm_user_assigned_identity" "aca" {
  name                = "${var.container_app_name}-id"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

resource "azurerm_role_assignment" "aca_kv_secrets_user" {
  scope                = var.key_vault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.aca.principal_id
}

resource "azurerm_role_assignment" "aca_blob_contributor" {
  scope                = var.storage_account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.aca.principal_id
}

resource "azurerm_role_assignment" "aca_blob_delegator" {
  scope                = var.storage_account_id
  role_definition_name = "Storage Blob Delegator"
  principal_id         = azurerm_user_assigned_identity.aca.principal_id
}

# AAD role propagation can take ~30-60s. Without this, the very first
# container app revision can fail to resolve KV secret references (403) even
# though the role assignment exists in ARM.
resource "time_sleep" "wait_for_rbac" {
  depends_on = [
    azurerm_role_assignment.aca_kv_secrets_user,
    azurerm_role_assignment.aca_blob_contributor,
    azurerm_role_assignment.aca_blob_delegator,
  ]
  create_duration = "60s"
}

resource "azurerm_container_app_environment" "this" {
  name                       = var.container_app_environment_name
  resource_group_name        = var.resource_group_name
  location                   = var.location
  log_analytics_workspace_id = var.log_analytics_workspace_id

  # Consumption-only environment — no `workload_profile` blocks declared, so
  # the env defaults to the pure Consumption plan with scale-to-zero.

  tags = var.tags
}

resource "azurerm_container_app" "this" {
  name                         = var.container_app_name
  container_app_environment_id = azurerm_container_app_environment.this.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.aca.id]
  }

  # ACR pulls authenticated via the UAMI. Only emitted when a registry
  # server is supplied — keeps the placeholder/MCR/ghcr.io path clean.
  # The UAMI must hold AcrPull on the registry; that role assignment is
  # owned by the caller (see dev/main.tf -> azurerm_role_assignment.aca_acr_pull).
  dynamic "registry" {
    for_each = var.container_registry_server == "" ? toset([]) : toset([var.container_registry_server])
    content {
      server   = registry.value
      identity = azurerm_user_assigned_identity.aca.id
    }
  }

  # KV-backed secrets — one block per logical name. The UAMI (already granted
  # "Key Vault Secrets User" above, with time_sleep for propagation) resolves
  # these at revision-deploy time.
  dynamic "secret" {
    for_each = var.kv_secret_ids
    content {
      name                = secret.key
      key_vault_secret_id = secret.value
      identity            = azurerm_user_assigned_identity.aca.id
    }
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    container {
      name   = "api"
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      # Standardize the .NET listening port in the cloud. Local dev still
      # uses 5105 via Program.cs default; Azure overrides here.
      env {
        name  = "ASPNETCORE_URLS"
        value = "http://+:${var.target_port}"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "DevelopmentAzure"
      }

      # Hint to DefaultAzureCredential to use the UAMI we attached, not a
      # different MI on the host.
      env {
        name  = "AZURE_CLIENT_ID"
        value = azurerm_user_assigned_identity.aca.client_id
      }

      # Plain (non-secret) env vars from the caller.
      dynamic "env" {
        for_each = var.extra_env
        content {
          name  = env.key
          value = env.value
        }
      }

      # App Insights — fine to ship as plain env (it's a connection string,
      # not a secret in the usual sense; the ingestion key in it has a tiny
      # blast radius — metric pollution, not data exfil).
      dynamic "env" {
        for_each = var.app_insights_connection_string == "" ? toset([]) : toset(["ai"])
        content {
          name  = "APPLICATIONINSIGHTS_CONNECTION_STRING"
          value = var.app_insights_connection_string
        }
      }

      # Secret-backed env vars — one per kv_env_mapping entry.
      dynamic "env" {
        for_each = var.kv_env_mapping
        content {
          name        = env.value
          secret_name = env.key
        }
      }
    }
  }

  ingress {
    external_enabled           = true
    target_port                = var.target_port
    transport                  = "auto"
    allow_insecure_connections = false

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  tags = var.tags

  depends_on = [time_sleep.wait_for_rbac]

  lifecycle {
    # pg-devops-cicd will flip the image between the placeholder and the
    # real GHCR image outside Terraform. Don't fight that.
    ignore_changes = [
      template[0].container[0].image,
    ]
  }
}
