# PhotoGallery — Terraform

Infrastructure-as-code for PhotoGallery's Azure footprint.

```
terraform/
├── bootstrap/                  # one-time state backend bootstrap
│   └── bootstrap-state.ps1
├── modules/                    # reusable building blocks
│   ├── storage/                # Azure Storage Account + blob container
│   ├── sql/                    # Azure SQL Server + Database (AAD-only)
│   ├── keyvault/               # RBAC-mode Key Vault + seed secrets
│   └── observability/          # Log Analytics + App Insights
└── dev/                        # dev environment composition
    ├── main.tf                 # wires modules together
    ├── variables.tf
    ├── outputs.tf
    ├── backend.dev.hcl.example
    └── terraform.tfvars.example
```

`prod/` will reuse the same modules with different SKUs / hardening (private
endpoints, no public IP firewall rule, geo-redundant storage, etc.). Not in
scope for this branch.

See [Documentation/Runbooks/local-azure-dev.md](../Documentation/Runbooks/local-azure-dev.md)
for the developer workflow.
