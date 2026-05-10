###############################################################################
# SQL module — Azure SQL Server + Database for PhotoGallery dev
#
# Design decisions (see DESIGN_DECISIONS.md):
#   * Single Azure SQL Database, **Basic** (5 DTU, 2 GB) ~ $5/mo flat — the
#     cheapest tier that still supports AAD-only auth and EF Core
#     migration-on-startup (per D013). Bump to S0 (~$15/mo) when dev data
#     outgrows 2 GB, or to GP_S_Gen5_1 serverless if usage becomes very bursty.
#   * AAD-only authentication. SQL auth disabled. Dev signs in via
#     `Authentication=Active Directory Default` which DefaultAzureCredential
#     resolves from `az login`.
#   * Firewall: Azure-services rule + a single dev IP rule. No 0.0.0.0/0.
#   * Migration-on-startup: EF Core's `Database.Migrate()` works against this
#     SKU; first cold-start may take 30-60s.
###############################################################################

resource "azurerm_mssql_server" "this" {
  name                = var.server_name
  resource_group_name = var.resource_group_name
  location            = var.location
  version             = "12.0"

  # AAD-only — no SQL admin login/password
  azuread_administrator {
    login_username              = var.aad_admin_login
    object_id                   = var.aad_admin_object_id
    azuread_authentication_only = true
  }

  minimum_tls_version           = "1.2"
  public_network_access_enabled = true

  tags = var.tags
}

resource "azurerm_mssql_database" "this" {
  name           = var.database_name
  server_id      = azurerm_mssql_server.this.id
  sku_name       = var.sku_name # default Basic (5 DTU, 2 GB)
  max_size_gb    = var.max_size_gb
  collation      = "SQL_Latin1_General_CP1_CI_AS"
  zone_redundant = false

  # Dev-friendly: short backup retention to control cost
  short_term_retention_policy {
    retention_days = 7
  }

  tags = var.tags
}

# Allow other Azure services (App Service / runners) to reach the server later
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Dev's public IP
resource "azurerm_mssql_firewall_rule" "dev_ip" {
  count            = var.dev_public_ip == "" ? 0 : 1
  name             = "DevLaptop"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = var.dev_public_ip
  end_ip_address   = var.dev_public_ip
}
