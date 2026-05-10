# MinIO CORS bootstrap for SPA fetch-streaming (PR-F)
#
# The cart download flow now runs entirely in the browser:
#   manifest endpoint -> short-lived MinIO URLs -> fetch(url).body -> client-zip
#   -> showSaveFilePicker / blob.
#
# `fetch(url)` from the SPA origin (http://localhost:4300) hits MinIO at
# http://localhost:9000. Without CORS, the browser blocks the response — the
# old `<a href download>` flow worked because plain navigation doesn't enforce
# CORS, but the streaming path does.
#
# This script configures MinIO's bucket-level CORS to accept the SPA origin
# (or `*` in dev). It's idempotent; safe to re-run after `docker compose up`.
#
# Usage:
#   .\scripts\setup-minio-cors.ps1                    # uses sensible defaults
#   .\scripts\setup-minio-cors.ps1 -Origin "*"        # dev wildcard
#   .\scripts\setup-minio-cors.ps1 -Bucket photogallery -Endpoint http://localhost:9000
#
# Production note (Azure Storage): see Documentation/Architecture/STORAGE_LAYER.md
# for the equivalent `az storage cors add` command — production migration is
# tracked in PR-G.

[CmdletBinding()]
param(
    [string]$Endpoint = "http://localhost:9000",
    [string]$AccessKey = "minioadmin",
    [string]$SecretKey = "minioadmin-password",
    [string]$Bucket = "photogallery",
    [string]$Origin = "http://localhost:4300",
    [string]$Alias = "pg-local"
)

$ErrorActionPreference = "Stop"

function Test-McAvailable {
    try {
        $null = & mc --version 2>$null
        return $true
    } catch {
        return $false
    }
}

if (-not (Test-McAvailable)) {
    Write-Warning "MinIO Client (mc) not found on PATH."
    Write-Host ""
    Write-Host "Install with one of:"
    Write-Host "  scoop install mc"
    Write-Host "  choco install minio-client"
    Write-Host "  https://min.io/docs/minio/linux/reference/minio-mc.html"
    Write-Host ""
    Write-Host "Or run the equivalent inside the running MinIO container:"
    Write-Host ("  docker compose exec minio sh -c " +
        "'mc alias set local http://localhost:9000 $AccessKey $SecretKey && " +
        "mc anonymous set download local/$Bucket'")
    exit 1
}

Write-Host "Configuring mc alias '$Alias' -> $Endpoint..."
& mc alias set $Alias $Endpoint $AccessKey $SecretKey | Out-Null

# Make objects in the bucket anonymously downloadable. (Presigned URLs already
# work without this, but it documents intent and unblocks dev tools that don't
# pre-sign.)
Write-Host "Setting bucket '$Bucket' anonymous download policy..."
& mc anonymous set download "$Alias/$Bucket" 2>&1 | ForEach-Object { Write-Host "  $_" }

# CORS rule: allow the SPA origin to fetch objects (GET/HEAD) and read response.
$corsJson = @{
    CORSRules = @(
        @{
            AllowedOrigins = @($Origin)
            AllowedMethods = @("GET", "HEAD")
            AllowedHeaders = @("*")
            ExposeHeaders  = @("Content-Length", "Content-Type", "ETag")
            MaxAgeSeconds  = 3600
        }
    )
} | ConvertTo-Json -Depth 5

$corsFile = Join-Path $env:TEMP "pg-minio-cors.json"
Set-Content -Path $corsFile -Value $corsJson -Encoding UTF8

try {
    Write-Host "Applying CORS rule (origin: $Origin) to '$Bucket'..."
    & mc cors set $corsFile "$Alias/$Bucket" 2>&1 | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "Done. SPA fetch-streaming should now work for the cart download flow." -ForegroundColor Green
} finally {
    Remove-Item $corsFile -ErrorAction SilentlyContinue
}
