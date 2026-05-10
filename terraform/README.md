# PhotoGallery — Terraform

Infrastructure-as-code for PhotoGallery's Azure footprint.

```
terraform/
├── bootstrap/                  # one-time state backend bootstrap
│   └── bootstrap-state.ps1
├── modules/                    # reusable building blocks
│   ├── storage/                # Azure Storage Account + blob container
│   ├── sql/                    # Azure SQL Server + Database (AAD-only, Basic SKU)
│   ├── keyvault/               # RBAC-mode Key Vault + seed secrets
│   ├── compute/                # Container Apps env + API container app (UAMI, KV refs)
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

## Cost guard (dev tier, idle)

| Resource | SKU | Approx. $/mo |
|----------|-----|--------------|
| Storage Account | Standard_LRS, Hot | <$1 (a few GB) |
| Azure SQL Database | **Basic** (5 DTU, 2 GB cap) | **~$5** |
| Key Vault | Standard | <$1 |
| Container Apps Environment + App | **Consumption, scale-to-zero (min=0, max=1, 0.5 vCPU/1 GiB)** | **~$0** idle (pay per request) |
| Log Analytics + App Insights | first 5 GB free | $0 |
| **Total dev footprint, idle** | | **~$6–7/mo** |

Notes:

- **SQL Basic** caps at 2 GB and 5 DTU — fine for MVP metadata (album/photo
  rows, users, carts). When you outgrow it, bump `sql_sku_name = "S0"`
  (~$15/mo, 250 GB) in `terraform.tfvars`. Serverless (`GP_S_Gen5_1` with
  auto-pause) was rejected for default — see DESIGN_DECISIONS.md D013.
- **Container Apps Consumption** with `min_replicas = 0` truly scales to
  zero between requests. You pay per-request CPU/memory seconds (sub-cent
  at MVP traffic). Cold-start is ~1-3 s.
- **No registry cost** — placeholder image lives on MCR (anonymous), real
  PhotoGallery backend image lives on **ghcr.io** as a public package
  (free). ACR Basic ($5/mo) is only added if/when private images become a
  requirement.
- **Frontend hosting** is out of scope for this Terraform pass. Cheapest
  path will be **Azure Static Web Apps Free tier** (0 $/mo, generous
  bandwidth). Tracked as a follow-up.
