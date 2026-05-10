###############################################################################
# GitHub OIDC module — AAD app + service principal + federated credential(s)
# for GitHub Actions to authenticate to Azure without long-lived secrets.
#
# Design decisions (see DESIGN_DECISIONS.md D015):
#   * Workload Identity Federation over a client-secret service principal —
#     no credential rotation, nothing to leak in CI logs, GitHub issues a
#     short-lived OIDC token that Azure AD trades for an access token.
#   * One AAD app + one SP per environment (dev/prod). Role assignments
#     (AcrPush, AcrPull, KV access, etc.) are made at the composition root
#     against the SP's `principal_id`.
#   * Federated subjects are caller-supplied (map) so the same module can
#     gate prod behind GitHub Environments while dev simply trusts pushes
#     to refs/heads/main.
###############################################################################

resource "azuread_application" "this" {
  display_name = var.display_name
}

resource "azuread_service_principal" "this" {
  client_id = azuread_application.this.client_id
}

resource "azuread_application_federated_identity_credential" "this" {
  for_each = var.subjects

  application_id = azuread_application.this.id
  display_name   = "github-${each.key}"
  description    = "GitHub Actions OIDC for ${var.github_repository} (${each.key})"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = "https://token.actions.githubusercontent.com"
  subject        = each.value
}
