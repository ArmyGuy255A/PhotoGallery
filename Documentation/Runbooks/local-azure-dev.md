# Local Development Against Real Azure Services

This runbook gets a developer from a clean machine to a local PhotoGallery
backend running against an Azure-hosted Storage Account, Azure SQL Database,
and Key Vault — using AAD auth end to end. **No connection strings or account
keys live on disk.**

## Why

We need to validate `IStorageProvider`, EF Core migrations, and the
Key-Vault-backed config provider against the real Azure data plane *before*
shipping to App Service / AKS. Running the app locally + Azure data plane is
the cheapest way to do that (~$17/mo, see cost section).

## 0. One-time install

| Tool | Version | Notes |
|------|---------|-------|
| Azure CLI (`az`) | 2.60+ | `winget install Microsoft.AzureCLI` |
| Terraform | 1.6+ | `winget install HashiCorp.Terraform` |
| .NET SDK | 9.0+ | already required for the repo |
| PowerShell 7+ | | for the bootstrap script |
| (optional) `sqlcmd` | latest | troubleshooting AAD logins to SQL |

You do **not** need SqlPackage. EF Core's `Database.Migrate()` runs migrations
against the Azure SQL DB on app startup just like it does locally.

## 1. Bootstrap the dev resource group + Terraform state backend (one-time, per subscription)

```powershell
az login
az account set --subscription 4fc243fa-5de2-48cb-9c98-793701d13152

cd terraform/bootstrap
./bootstrap-state.ps1
```

The script defaults to the pinned PhotoGallery dev subscription
(`4fc243fa-5de2-48cb-9c98-793701d13152`); pass `-SubscriptionId <other>` only
if you're working in a fork.

This creates:

- `PhotoGallery-dev` resource group (single RG holding **both** tfstate and
  the workload — see DESIGN_DECISIONS.md D012)
- `stpgtfstate<6-char-hash>` state Storage Account (versioning + soft-delete on)
- `tfstate` container
- Role assignment: your user → **Storage Blob Data Contributor** on the state SA
- Writes `terraform/dev/backend.dev.hcl` with the resolved values

Idempotent — safe to rerun.

## 2. Provision the dev footprint

```powershell
cd terraform/dev

# Discover your IDs (PowerShell)
$env:DEV_OBJECT_ID = az ad signed-in-user show --query id -o tsv
$env:DEV_UPN       = az ad signed-in-user show --query userPrincipalName -o tsv
$env:DEV_IP        = (Invoke-RestMethod -Uri https://api.ipify.org)
$env:SUB_ID        = az account show --query id -o tsv

Copy-Item terraform.tfvars.example terraform.tfvars
```

Then edit `terraform.tfvars`:

```hcl
# subscription_id and resource_group_name have sensible defaults — override
# only if you're working in a fork or want a non-default RG name. The RG must
# start with "PhotoGallery".
subscription_id         = "4fc243fa-5de2-48cb-9c98-793701d13152"
resource_group_name     = "PhotoGallery-dev"
dev_principal_object_id = "<DEV_OBJECT_ID>"
aad_admin_login         = "<DEV_UPN>"
dev_public_ip           = "<DEV_IP>"
owner_tag               = "<DEV_UPN>"
```

```powershell
terraform init -backend-config=backend.dev.hcl
terraform plan  -out=dev.tfplan
terraform apply dev.tfplan
```

Apply takes ~5 min (SQL Server is the slow one). When it's done, capture
outputs:

```powershell
terraform output
```

## 3. Replace the placeholder secrets in Key Vault

The TF run seeded Key Vault with real values for SQL connection string and
Storage account name, but **dummy placeholders** for Google OAuth, JWT signing
key, and ACS. Set those manually:

```powershell
$kv = terraform output -raw key_vault_name

# Google OAuth (from https://console.cloud.google.com)
az keyvault secret set --vault-name $kv --name "Auth--Google--ClientId"     --value "<...>"
az keyvault secret set --vault-name $kv --name "Auth--Google--ClientSecret" --value "<...>"

# JWT signing key — generate a strong one
$jwtKey = [Convert]::ToBase64String((1..64 | ForEach-Object { [byte](Get-Random -Max 256) }))
az keyvault secret set --vault-name $kv --name "Auth--Jwt--SigningKey" --value $jwtKey

# Azure Communication Services connection string (when you wire email)
az keyvault secret set --vault-name $kv --name "Acs--ConnectionString" --value "<...>"
```

The Terraform module ignores changes to secret `value`, so re-applying TF
won't clobber these.

## 4. Wire the local app

### 4a. Switch the storage provider

In `PhotoGallery/appsettings.Development.json` (or User Secrets), set:

```json
{
  "Storage": {
    "Type": "Azure",
    "Azure": {
      "AccountName":   "<terraform output storage_account_name>",
      "BlobEndpoint":  "<terraform output blob_endpoint>",
      "ContainerName": "photogallery"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "<terraform output sql_server_fqdn-derived; OR pull from KV>"
  },
  "KeyVault": {
    "Uri": "<terraform output key_vault_uri>"
  }
}
```

> **Note:** the existing `AzureStorageProvider.cs` reads
> `Storage:Azure:ConnectionString`. The backend dev refactor (separate work
> item) replaces that with `BlobServiceClient(new Uri(blobEndpoint), new DefaultAzureCredential())`
> + user-delegation SAS for `GetUrlAsync`. Until that lands, you can fall back
> to a temporary connection string (storage account keys are disabled by TF —
> you'd need to flip `shared_access_key_enabled = true` in
> `terraform/modules/storage/main.tf`, but **don't commit that**).

### 4b. Add the Key Vault config provider

In `Program.cs`, before `builder.Build()`:

```csharp
var kvUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrEmpty(kvUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(kvUri),
        new DefaultAzureCredential());
}
```

The provider maps `Sql--ConnectionString` → `Sql:ConnectionString`,
`Auth--Google--ClientSecret` → `Auth:Google:ClientSecret`, etc.

### 4c. Sign in once per session

```powershell
az login
```

`DefaultAzureCredential` will pick up your `az` token automatically (via the
AzureCliCredential leg). No env vars required for the happy path.

## 5. Run the app

```powershell
cd PhotoGallery
dotnet run
```

Expected behavior:

1. App starts, reads `KeyVault:Uri`, hydrates `Sql:ConnectionString`,
   `Auth:Google:*`, `Auth:Jwt:SigningKey`.
2. EF Core runs `Database.Migrate()` against Azure SQL — first run takes
   30–60s while the DB warms up.
3. Upload a photo through the UI → blob lands in
   `https://<account>.blob.core.windows.net/photogallery/...`.
4. Browser-side photo URL is a user-delegation SAS (URL contains
   `?sv=...&skoid=...&sks=b&sig=...`) valid for 60 min.

## 6. Tear down

```powershell
cd terraform/dev
terraform destroy
```

`terraform destroy` only removes the workload resources it manages (storage,
SQL, Key Vault, observability). The `PhotoGallery-dev` resource group itself
and the state Storage Account inside it survive — they were created out of
band by the bootstrap script and are intentionally **not** under Terraform
management (see DESIGN_DECISIONS.md D012). Leave them for the next
provisioning cycle. If you really want a clean slate, run
`az group delete --name PhotoGallery-dev --yes` afterward — but you'll have
to rerun the bootstrap to re-provision.

## Troubleshooting

| Symptom | Cause / Fix |
|---------|-------------|
| `AADSTS70011` from KV | Wrong tenant. Run `az login --tenant <id>`. |
| `Login failed for user '<token>'` on SQL | Your IP isn't allowlisted. Re-run `terraform apply` with the new `dev_public_ip`. |
| `403 AuthorizationPermissionMismatch` on blob | Role assignment hasn't propagated (~1–5 min). Wait, then retry. |
| `KeyVaultErrorException: Forbidden` | Same — wait for RBAC propagation. |
| App boots but can't see secrets | `KeyVault:Uri` not set, or secret name uses `:` instead of `--`. |
| EF Core migration hangs | First-time S0 cold start can take 60s. If >2 min, check the firewall rule. |

## Cost guard

| Resource | SKU | Approx. $/mo |
|----------|-----|--------------|
| Storage Account | Standard_LRS, Hot | <$1 (a few GB) |
| Azure SQL Database | S0 (10 DTU, 250 GB cap) | ~$15 |
| Key Vault | Standard | <$1 |
| Log Analytics + App Insights | first 5 GB free | $0 |
| **Total dev footprint** | | **~$17/mo** |

If you stop dev'ing for a few weeks, run `terraform destroy` and recreate
later — the apply is ~5 min.
