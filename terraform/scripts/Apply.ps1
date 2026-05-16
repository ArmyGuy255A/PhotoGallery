<#
.SYNOPSIS
  End-to-end "stand up the trial environment" helper: terraform apply +
  the post-apply SQL principal registration. Replaces the manual steps
  documented in Documentation/Runbooks/local-azure-dev.md so the operator
  only needs `az login`.

.DESCRIPTION
  Runs in this order:
    1. terraform init (no-op on the second call)
    2. terraform apply -auto-approve -var-file=terraform.tfvars
    3. Read outputs (sql_server_fqdn, sql_database, container_app_uami_name,
       worker_uami_name).
    4. Register every UAMI as a SQL DB user via Register-SqlPrincipals.ps1.

  Idempotent — re-running on a converged environment is a couple of seconds
  of "no changes" + a couple of no-op T-SQL statements.

.PARAMETER VarFile
  Path to the .tfvars file (default ./terraform.tfvars).

.PARAMETER BackendConfig
  Path to the -backend-config file used by `terraform init` (default
  ./backend.dev.hcl).

.PARAMETER SkipInit
  Set when you've already initialised the working directory.

.EXAMPLE
  cd terraform/dev
  ../scripts/Apply.ps1
#>
[CmdletBinding()]
param(
    [string] $VarFile = './terraform.tfvars',
    [string] $BackendConfig = './backend.dev.hcl',
    [switch] $SkipInit,
    [switch] $SkipIpDetect
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $VarFile)) {
    throw "VarFile '$VarFile' not found. Run from terraform/dev or pass -VarFile."
}

# Auto-detect the dev laptop's public IP so the SQL firewall rule survives
# moving between WiFi networks / ISP lease changes. Without this, every
# IP shuffle destroys the dev firewall rule on apply, then locks the operator
# out of the DB until they manually re-add it. Pass -SkipIpDetect to opt out
# (e.g. when running from a static-IP VPN that's already in the rule list).
$extraVars = @()
if (-not $SkipIpDetect) {
    try {
        $ip = (Invoke-RestMethod https://api.ipify.org -TimeoutSec 5).Trim()
        if ($ip -match '^\d+\.\d+\.\d+\.\d+$') {
            Write-Host "[apply] Detected public IP $ip — passing as -var dev_public_ip" -ForegroundColor DarkGray
            $extraVars = @("-var", "dev_public_ip=$ip")
        }
    }
    catch {
        Write-Warning "Could not detect public IP via api.ipify.org: $($_.Exception.Message). SQL firewall rule will not be set."
    }
}

if (-not $SkipInit) {
    Write-Host "[apply] terraform init…" -ForegroundColor Cyan
    if (Test-Path $BackendConfig) {
        terraform init -backend-config="$BackendConfig" -upgrade
    } else {
        Write-Warning "BackendConfig '$BackendConfig' not found; running terraform init without backend config."
        terraform init -upgrade
    }
    if ($LASTEXITCODE -ne 0) { throw "terraform init failed." }
}

Write-Host "[apply] terraform apply…" -ForegroundColor Cyan
$applyArgs = @('apply', '-auto-approve', "-var-file=$VarFile") + $extraVars
& terraform @applyArgs
if ($LASTEXITCODE -ne 0) { throw "terraform apply failed." }

Write-Host "[apply] Reading outputs…" -ForegroundColor Cyan
$tfJson = terraform output -json | ConvertFrom-Json

$sqlServer = $tfJson.sql_server_fqdn.value
$sqlDb     = $tfJson.sql_database.value
$apiUami    = $tfJson.container_app_uami_name.value
$workerUami = $null
if ($tfJson.PSObject.Properties.Name -contains 'worker_container_app_uami_name') {
    $workerUami = $tfJson.worker_container_app_uami_name.value
}

if (-not $sqlServer -or -not $sqlDb) {
    throw "Terraform did not expose sql_server_fqdn / sql_database outputs."
}

$principals = @($apiUami)
if ($workerUami) { $principals += $workerUami }

Write-Host "[apply] Registering SQL principals: $($principals -join ', ')" -ForegroundColor Cyan
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$scriptDir/Register-SqlPrincipals.ps1" `
    -SqlServer $sqlServer `
    -SqlDatabase $sqlDb `
    -PrincipalNames $principals

Write-Host "[apply] Done. Environment is ready." -ForegroundColor Green
