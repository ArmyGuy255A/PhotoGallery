<#
.SYNOPSIS
  Register an Azure AD principal (user-assigned managed identity, group, or user)
  as a SQL DB principal with the standard PhotoGallery role set. Idempotent.

.DESCRIPTION
  Replaces the manual T-SQL block in Documentation/Runbooks/local-azure-dev.md
  (step 3a) and unblocks fully-automated terraform apply runs.

  For each principal name passed in:
    1. CREATE USER [<name>] FROM EXTERNAL PROVIDER  (skipped if already exists)
    2. ALTER ROLE db_datareader / db_datawriter / db_ddladmin ADD MEMBER [<name>]
       (db_ddladmin is required so EF Core's Database.Migrate() can issue DDL on
       startup; without it the first request after a fresh DB fails with
       "CREATE TABLE permission denied".)

  Authenticates to SQL with the caller's az-login (ActiveDirectoryDefault).
  The caller must already hold the AAD admin role on the SQL server (the dev
  principal_object_id in terraform/dev). If sqlcmd is missing, falls back to
  Invoke-Sqlcmd from the SqlServer PowerShell module.

.PARAMETER SqlServer
  FQDN of the Azure SQL server, e.g. sql-photogallery-dev-xxxx-cu.database.windows.net.

.PARAMETER SqlDatabase
  DB name (e.g. photogallery).

.PARAMETER PrincipalNames
  One or more principal names to register. For a UAMI, this is the UAMI name
  exactly as it appears in Azure (e.g. ca-photogallery-api-dev-id). Case-
  sensitive at the AAD lookup layer.

.EXAMPLE
  ./Register-SqlPrincipals.ps1 `
    -SqlServer sql-photogallery-dev-1234-cu.database.windows.net `
    -SqlDatabase photogallery `
    -PrincipalNames ca-photogallery-api-dev-id, ca-photogallery-worker-dev-id

.NOTES
  Idempotent — safe to re-run on every `terraform apply`. The runbook still
  documents the manual flow as a fallback when the script can't run (e.g.
  no sqlcmd, no SqlServer module, no az session).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $SqlServer,
    [Parameter(Mandatory = $true)] [string] $SqlDatabase,
    [Parameter(Mandatory = $true)] [string[]] $PrincipalNames
)

$ErrorActionPreference = 'Stop'

function Invoke-SqlScript {
    param([string] $Script)

    # We want a non-interactive auth path that works the same way under
    # `az login` locally and under azure/login@v2 in CI. The cleanest path
    # is: mint a SQL access token via `az account get-access-token` and
    # pass it through sqlcmd's `-G --authentication-method ActiveDirectoryServicePrincipal`
    # equivalent — or use Invoke-Sqlcmd's -AccessToken which is exactly
    # what we want. sqlcmd's -G alone (without -P) prompts for a password,
    # which deadlocks the script in CI / non-interactive sessions.
    Write-Verbose "Fetching SQL access token via az account get-access-token…"
    $token = (az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv) 2>$null
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "Failed to acquire SQL access token via 'az account get-access-token'. Run 'az login' first."
    }

    # Prefer Invoke-Sqlcmd because it accepts -AccessToken natively and
    # has no interactive-prompt failure mode. Install the SqlServer module
    # on first use if needed.
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Write-Host "Installing SqlServer PowerShell module (CurrentUser scope)…" -ForegroundColor DarkGray
        Install-Module -Name SqlServer -Scope CurrentUser -Force -AllowClobber -Repository PSGallery
    }
    Import-Module SqlServer -ErrorAction Stop

    Invoke-Sqlcmd `
        -ServerInstance $SqlServer `
        -Database $SqlDatabase `
        -AccessToken $token `
        -Query $Script `
        -ErrorAction Stop `
        -OutputSqlErrors $true
}

foreach ($principal in $PrincipalNames) {
    if ([string]::IsNullOrWhiteSpace($principal)) {
        Write-Warning "Skipping empty principal name."
        continue
    }

    Write-Host "[Register-SqlPrincipals] Registering '$principal' on $SqlServer/$SqlDatabase…" -ForegroundColor Cyan

    # T-SQL guard: ``IF NOT EXISTS`` makes CREATE USER idempotent. The ALTER
    # ROLE calls are themselves idempotent — re-adding a member is a no-op.
    # Brackets quote the name so dashes / spaces don't trip the parser.
    #
    # Two separate batches because CREATE USER must commit before ALTER ROLE
    # can reference the principal — and Invoke-Sqlcmd's GO handling differs
    # across SqlServer module versions, so we split into two explicit calls.
    $createUser = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$principal')
BEGIN
    EXEC('CREATE USER [$principal] FROM EXTERNAL PROVIDER');
    PRINT 'Created DB user [$principal]';
END
ELSE
BEGIN
    PRINT 'DB user [$principal] already exists; ensuring role membership only.';
END
"@

    $grantRoles = @"
ALTER ROLE db_datareader ADD MEMBER [$principal];
ALTER ROLE db_datawriter ADD MEMBER [$principal];
ALTER ROLE db_ddladmin   ADD MEMBER [$principal];
PRINT 'Granted db_datareader / db_datawriter / db_ddladmin to [$principal]';
"@

    Invoke-SqlScript -Script $createUser
    Invoke-SqlScript -Script $grantRoles
}

Write-Host "[Register-SqlPrincipals] All principals registered." -ForegroundColor Green
