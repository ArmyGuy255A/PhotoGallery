output "server_fqdn" {
  value = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "database_name" {
  value = azurerm_mssql_database.this.name
}

# AAD-auth ADO.NET style. App pairs with `Authentication=Active Directory Default`
# to let DefaultAzureCredential drive the token.
output "aad_connection_string" {
  value = format(
    "Server=tcp:%s,1433;Initial Catalog=%s;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Default;",
    azurerm_mssql_server.this.fully_qualified_domain_name,
    azurerm_mssql_database.this.name,
  )
  sensitive = false
}
