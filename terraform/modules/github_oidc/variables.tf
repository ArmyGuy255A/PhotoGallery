variable "display_name" {
  description = "Display name for the AAD application backing GitHub Actions OIDC (e.g. 'photogallery-github-actions-dev')."
  type        = string
}

variable "github_repository" {
  description = "GitHub repository in `owner/repo` form (e.g. 'ArmyGuy255A/PhotoGallery'). Used to scope the federated credential subject."
  type        = string

  validation {
    condition     = can(regex("^[^/]+/[^/]+$", var.github_repository))
    error_message = "github_repository must be in 'owner/repo' form."
  }
}

variable "subjects" {
  description = <<-EOT
    Map of federated identity credential subjects to provision. The key is a
    short stable name (used in the credential resource name); the value is the
    GitHub OIDC subject claim, e.g.:
      "main"          = "repo:ArmyGuy255A/PhotoGallery:ref:refs/heads/main"
      "pull-request"  = "repo:ArmyGuy255A/PhotoGallery:pull_request"
      "env-prod"      = "repo:ArmyGuy255A/PhotoGallery:environment:prod"
    See https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/about-security-hardening-with-openid-connect#example-subject-claims
  EOT
  type        = map(string)
}
