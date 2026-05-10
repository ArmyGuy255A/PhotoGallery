# Local Development Against Real Azure Services

This runbook gets a developer from a clean machine to a local PhotoGallery
backend running against an Azure-hosted Storage Account, Azure SQL Database,
and Key Vault — using AAD auth end to end. **No connection strings or account
keys live on disk.**

## Why

We need to validate `IStorageProvider`, EF Core migrations, and the
Key-Vault-backed config provider against the real Azure data plane *before*
shipping to production. Running the app locally against the Azure data
plane — and now optionally hitting a (mostly idle) Container Apps deployment
of the API — is the cheapest way to do that (~$6–7/mo idle, see cost
section).

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

Apply takes ~8-10 min (SQL Server + Container Apps Environment are the slow
ones). When it's done, capture outputs:

```powershell
terraform output
```

> **First-apply note (Container Apps + KV references).** The container app's
> user-assigned MI is granted `Key Vault Secrets User` during this apply, and
> we wait 60 s (`time_sleep`) for AAD propagation before deploying the first
> revision. Most of the time this Just Works. If the very first apply fails
> with a 403 resolving a KV secret on the container app revision, simply
> re-run `terraform apply` — the role is already in place by then and the
> second apply succeeds cleanly.

## 3. Replace the placeholder secrets in Key Vault

The TF run seeded Key Vault with real values for the SQL connection string
and the storage AccountUrl, but **`<TO-BE-SET>` placeholders** for Google
OAuth, JWT signing key, and ACS. Set those manually:

```powershell
$kv = terraform output -raw key_vault_name

# Google OAuth (from https://console.cloud.google.com)
az keyvault secret set --vault-name $kv --name "Authentication--Google--ClientId"     --value "<...>"
az keyvault secret set --vault-name $kv --name "Authentication--Google--ClientSecret" --value "<...>"

# JWT signing key — generate a strong one
$jwtKey = [Convert]::ToBase64String((1..64 | ForEach-Object { [byte](Get-Random -Max 256) }))
az keyvault secret set --vault-name $kv --name "Authentication--Jwt--Key" --value $jwtKey

# Azure Communication Services connection string (when you wire email)
az keyvault secret set --vault-name $kv --name "Email--AzureCommunicationServices--ConnectionString" --value "<...>"
```

The Terraform module ignores changes to secret `value`, so re-applying TF
won't clobber these.

### Key Vault secret contract

Backend dev (`pg-aspnet-backend-dev`) owns the canonical secret-name contract.
Terraform creates exactly these names; the .NET Key Vault config provider
translates `--` → `:` so each lines up with an ASP.NET Core config path. Do
not deviate from this table — see `PhotoGallery/ConfigurationCanonicalAliases.cs`
for the .NET-side source of truth.

| KV secret name | .NET config path (after `--`→`:`) | Seeded by TF |
|---|---|---|
| `ConnectionStrings--DefaultConnection` | `ConnectionStrings:DefaultConnection` | real value (composed from SQL output) |
| `Storage--AzureBlob--AccountUrl` | `Storage:AzureBlob:AccountUrl` (optional in KV; also in appsettings) | real value (blob endpoint) |
| `Authentication--Google--ClientId` | `Authentication:Google:ClientId` | `<TO-BE-SET>` placeholder |
| `Authentication--Google--ClientSecret` | `Authentication:Google:ClientSecret` | `<TO-BE-SET>` placeholder |
| `Authentication--Jwt--Key` | `Authentication:Jwt:Key` | `<TO-BE-SET>` placeholder |
| `Email--AzureCommunicationServices--ConnectionString` | `Email:AzureCommunicationServices:ConnectionString` | `<TO-BE-SET>` placeholder |

ACA bridges these into the container as env vars named with `__` (double
underscore), which .NET also binds to `:`. E.g. KV secret
`ConnectionStrings--DefaultConnection` → ACA secret alias
`connectionstrings-defaultconnection` → container env var
`ConnectionStrings__DefaultConnection`. The three forms (`--` in KV, `-` in
ACA alias, `__` in env) all collapse to the same .NET config path at runtime.

## 3a. Register the Container App's UAMI in Azure SQL (one-time, manual)

The Container App is provisioned with a **user-assigned managed identity**
(`<container_app_uami_name>` from `terraform output`). Terraform can't run
T-SQL cleanly, so you have to grant the UAMI access to the database
manually — once per fresh DB.

You sign into the SQL server as the AAD admin (your dev user) and add the
UAMI as an EXTERNAL PROVIDER user:

```powershell
$server  = (terraform output -raw sql_server_fqdn)
$db      = (terraform output -raw sql_database_name)
$uami    = (terraform output -raw container_app_uami_name)

# Use sqlcmd with AAD interactive auth (or 'ActiveDirectoryDefault' if signed in via az)
sqlcmd -S $server -d $db -G -U "$env:DEV_UPN" -Q @"
CREATE USER [$uami] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [$uami];
ALTER ROLE db_datawriter ADD MEMBER [$uami];
ALTER ROLE db_ddladmin   ADD MEMBER [$uami];  -- needed for EF Core migrations
"@
```

The `db_ddladmin` role is what lets `Database.Migrate()` issue DDL on
startup. Without it, the very first request after a fresh DB will fail with
"CREATE TABLE permission denied."

If you destroy and re-create the SQL DB later, redo this step — the UAMI
itself survives `terraform destroy` (it's tied to the container app, not the
DB), but the SQL-side `CREATE USER` does not.

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

The provider maps `ConnectionStrings--DefaultConnection` →
`ConnectionStrings:DefaultConnection`, `Authentication--Google--ClientSecret` →
`Authentication:Google:ClientSecret`, etc. See the "Key Vault secret contract"
table above for the full list.

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

## 5a. Container Registry (push the API image)

The dev footprint includes an **Azure Container Registry, Basic SKU**
(`acrpgdeva4pi.azurecr.io`). Pulls from ACA authenticate via the API's
UAMI (already granted `AcrPull` by Terraform). Pushes use AAD via your
own login (Terraform grants you `AcrPush` against `var.dev_principal_object_id`).

```powershell
# 1. Get the registry login server from Terraform outputs
$acr = (terraform -chdir=terraform/dev output -raw container_registry_login_server)
# -> acrpgdeva4pi.azurecr.io

# 2. Log in with AAD (no admin user, no PAT — just your az session)
az acr login --name acrpgdeva4pi

# 3. Build the backend image locally (from repo root)
docker build -f Dockerfile.backend -t "$acr/photogallery-backend:dev" .

# 4. Push
docker push "$acr/photogallery-backend:dev"

# 5. Point the running container app at the new image. The compute module
#    has `image` in lifecycle.ignore_changes, so this is an out-of-band CD
#    step (Terraform will not fight it on the next apply).
az containerapp update `
  --name ca-photogallery-api-dev `
  --resource-group PhotoGallery-dev `
  --image "$acr/photogallery-backend:dev"
```

Notes:

- `az acr login` against a Basic SKU registry uses the AAD-backed flow.
  It writes a short-lived (~3 h) access token into Docker's auth store.
  No long-lived registry credential ever exists locally.
- ACA's pull is also AAD: the `registry { server = "...azurecr.io",
  identity = <UAMI> }` block in the container app authenticates each
  pull against the UAMI's `AcrPull` role on the registry. There are no
  registry secrets in Key Vault or in the container app config.
- If you re-tag the same digest (e.g. push `:dev` repeatedly), ACA only
  pulls the new image when a new revision is created — the
  `az containerapp update --image` call above forces that.

## 6. Tear down

```powershell
cd terraform/dev
terraform destroy
```

`terraform destroy` only removes the workload resources it manages (storage,
SQL, Key Vault, observability, ACR). The `PhotoGallery-dev` resource group
itself and the state Storage Account inside it survive — they were created
out of band by the bootstrap script and are intentionally **not** under
Terraform management (see DESIGN_DECISIONS.md D012). Leave them for the next
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
| EF Core migration hangs | First-time SQL Basic cold start can take 30-60s. If >2 min, check the firewall rule. |
| Container app revision stuck in "Activation Failed" with KV 403 | Role propagation race on first apply. Re-run `terraform apply` — second apply succeeds because the UAMI's `Key Vault Secrets User` role is already in place. |
| `Login failed for user '<uami-name>'` from the container app | You skipped step 3a. Run the `CREATE USER ... FROM EXTERNAL PROVIDER` block. |
| `CREATE TABLE permission denied` from EF migrations in the container app | UAMI isn't in `db_ddladmin`. Re-run the role grants in step 3a. |

## Cost guard

| Resource | SKU | Approx. $/mo |
|----------|-----|--------------|
| Storage Account | Standard_LRS, Hot | <$1 (a few GB) |
| Azure SQL Database | **Basic** (5 DTU, 2 GB cap) | **~$5** |
| Key Vault | Standard | <$1 |
| Container Apps Environment + API app | **Consumption, scale-to-zero (min=0, max=1, 0.5 vCPU/1 GiB)** | **~$0** idle (pay per request) |
| Azure Container Registry | **Basic** (10 GB included, AAD auth, no geo-rep) | **~$5** |
| Log Analytics + App Insights | first 5 GB free | $0 |
| **Total dev footprint, idle** | | **~$11–12/mo** |

Notes:

- **SQL Basic** is the cheapest tier that supports AAD-only auth and EF
  Core migrations. The 2 GB / 5 DTU caps are fine for MVP metadata. When
  you outgrow it, set `sql_sku_name = "S0"` (~$15/mo, 250 GB) in
  `terraform.tfvars`. See DESIGN_DECISIONS.md D013.
- **Container Apps Consumption** with `min_replicas = 0` truly scales to
  zero between requests. You pay per-request CPU/memory seconds (sub-cent
  at MVP traffic). Cold-start ~1-3 s.
- **Azure Container Registry, Basic SKU** (~$5/mo flat). Hosts the
  PhotoGallery backend image. Pulls authenticated via the ACA UAMI
  (`AcrPull`); pushes via `az acr login` + `AcrPush` on the developer.
  Replaces the earlier ghcr.io path — see DESIGN_DECISIONS.md D014.
- **Frontend hosting** is out of scope for this Terraform pass. Cheapest
  path will be **Azure Static Web Apps Free tier**. Tracked as a follow-up.

If you stop dev'ing for a few weeks, run `terraform destroy` and recreate
later — the apply is ~8-10 min. (Idle ACA costs nothing, so keeping it up
between sessions is also fine.)

## Frontend hosting — Azure Static Web Apps (Free tier)

The Angular SPA is hosted on **Azure Static Web Apps Free**. The resource is
provisioned by `terraform/modules/staticwebapp` and lives in the same
`PhotoGallery-dev` RG as the rest of the footprint (per D012). $0/mo — free
SSL, global CDN, custom domains, GitHub Actions deploy.

### Live URL pattern

`https://<random-words>-<hash>.<n>.azurestaticapps.net`

After apply, read it from Terraform outputs:

```powershell
cd terraform/dev
terraform output static_web_app_url
# e.g. https://agreeable-tree-043fa290f.7.azurestaticapps.net
```

### Backend CORS wiring

`terraform/dev/main.tf` injects the SWA hostname into the API's ACA env vars:

- `Frontend__Url` — used by the existing `AllowFrontendDev` CORS policy
  (single-origin path). This is what unblocks the deployed image today.
- `Cors__AllowedOrigins__0` — first slot of the new `List<string>`
  `Cors:AllowedOrigins` binding. The new Program.cs unions this with
  `Frontend.Url` to build the CORS allowlist. Additional dev origins
  (e.g. `http://localhost:4200` when running the Angular dev server
  against the cloud backend) go through `var.frontend_origin_extra` in
  `terraform.tfvars`:

```hcl
frontend_origin_extra = ["http://localhost:4200"]
```

Each entry shows up as `Cors__AllowedOrigins__<N>` (N starts at 1).

> ⚠️ Updating the CORS list requires `terraform apply` — the env vars are
> baked into the ACA revision. Bouncing the revision is cheap (a few
> seconds). For a more dynamic story, move to the KV-backed option
> sketched in the design doc.

### Deploy API key

The SWA deploy API key (used by the FE GitHub Actions workflow) is a
sensitive Terraform output:

```powershell
terraform output -raw static_web_app_api_key
```

Store it as a repo secret (e.g. `AZURE_STATIC_WEB_APPS_API_TOKEN_DEV`) for
the deploy workflow. **Never commit it.** Rotate via the Azure portal or
`az staticwebapp secrets reset-api-key` if leaked.

### Verifying CORS end-to-end

```powershell
$swa = (terraform output -raw static_web_app_url)
$api = (terraform output -raw container_app_url)
curl -I -H "Origin: $swa" "$api/"
# Look for: access-control-allow-origin: https://<swa-host>
```
