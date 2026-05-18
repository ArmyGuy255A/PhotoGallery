<#
.SYNOPSIS
  Local-proxy smoke for the PhotoGallery dev stack.

.DESCRIPTION
  Runs the validation curls documented in
  Documentation/Runbooks/local-proxy-dev.md against the local stack:

    1. https://localhost:8000/healthz                            -> 200 "ok"
    2. https://localhost:8000/photogallery/api/healthz           -> 200
    3. https://localhost:8000/photogallery/api/config/public     -> 200 JSON
       (asserts body contains "googleClientId")
    4. POST .../photogallery/hubs/photo-progress/negotiate?...   -> 200 or 401
       (NOT 404; NOT HTML SPA fallback)

  Prints PASS / FAIL per check. Exits 0 if every check passed, 1 otherwise.
  Skips the browser check on purpose — the runbook covers that step.

.PARAMETER BaseUrl
  Optional override for the nginx edge (default https://localhost:8000).

.PARAMETER WhatIf
  Don't run curl; just print the checks that would run.

.EXAMPLE
  pwsh .\scripts\smoke-local-proxy.ps1

.EXAMPLE
  pwsh .\scripts\smoke-local-proxy.ps1 -BaseUrl https://localhost:8443

.NOTES
  Requires curl.exe (ships with Windows 10+ and pwsh on Linux/macOS via curl).
  Uses -k to skip self-signed cert verification — see the runbook for cert
  generation. Does NOT depend on any third-party PowerShell modules.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$BaseUrl = 'https://localhost:8000'
)

$ErrorActionPreference = 'Stop'

# ---------- helpers -----------------------------------------------------------

$script:Results = New-Object System.Collections.Generic.List[psobject]

function Add-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )
    $script:Results.Add([pscustomobject]@{
        Name   = $Name
        Passed = $Passed
        Detail = $Detail
    }) | Out-Null

    $tag = if ($Passed) { 'PASS' } else { 'FAIL' }
    $color = if ($Passed) { 'Green' } else { 'Red' }
    Write-Host ('[{0}] {1} — {2}' -f $tag, $Name, $Detail) -ForegroundColor $color
}

function Invoke-CurlCheck {
    <#
    .DESCRIPTION
      Runs curl.exe and returns a parsed result:
        @{ Status = <int>; Headers = <string>; Body = <string> }
      Status is 0 on transport failure (connection refused, TLS error, etc.).
    #>
    param(
        [Parameter(Mandatory)] [string]$Url,
        [string]$Method = 'GET'
    )

    $args = @('-sk', '-i', '-X', $Method, '--max-time', '10', $Url)
    # Use cmd's IO redirection via -ErrorAction; curl writes status + headers +
    # body all to stdout because of -i.
    $raw = & curl.exe @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{
            Status  = 0
            Headers = ''
            Body    = ($raw -join "`n")
            Error   = "curl exit $LASTEXITCODE"
        }
    }

    $text = $raw -join "`n"
    # Split on the first blank line — that's the headers/body boundary.
    $parts = $text -split "(?m)^\s*$", 2
    $headers = $parts[0]
    $body    = if ($parts.Length -gt 1) { $parts[1] } else { '' }

    $status = 0
    if ($headers -match 'HTTP/[\d\.]+\s+(\d{3})') {
        $status = [int]$Matches[1]
    }

    return [pscustomobject]@{
        Status  = $status
        Headers = $headers
        Body    = $body
        Error   = $null
    }
}

function Test-LooksLikeSpaFallback {
    param([string]$Body)
    return ($Body -match '<title>\s*Photo Gallery\s*</title>') -or
           ($Body -match '<base\s+href=')
}

# ---------- banner ------------------------------------------------------------

Write-Host ''
Write-Host '=== PhotoGallery local-proxy smoke ===' -ForegroundColor Cyan
Write-Host ("Edge: {0}" -f $BaseUrl)
Write-Host ("Runbook: Documentation/Runbooks/local-proxy-dev.md")
Write-Host ''

if ($WhatIfPreference) {
    Write-Host 'Dry run — would execute:' -ForegroundColor Yellow
    Write-Host ("  GET  {0}/healthz" -f $BaseUrl)
    Write-Host ("  GET  {0}/photogallery/api/healthz" -f $BaseUrl)
    Write-Host ("  GET  {0}/photogallery/api/config/public" -f $BaseUrl)
    Write-Host ("  POST {0}/photogallery/hubs/photo-progress/negotiate?negotiateVersion=1" -f $BaseUrl)
    Write-Host ''
    Write-Host 'Browser check is skipped by design (manual step in the runbook).'
    exit 0
}

# ---------- checks ------------------------------------------------------------

# A. nginx healthz
$r = Invoke-CurlCheck -Url "$BaseUrl/healthz"
$ok = ($r.Status -eq 200) -and ($r.Body.Trim() -eq 'ok')
$detail = if ($r.Error) { $r.Error }
          elseif ($ok) { 'HTTP 200, body "ok"' }
          else { ('HTTP {0}, body="{1}"' -f $r.Status, $r.Body.Trim()) }
Add-Result -Name 'A. nginx /healthz'                                 -Passed $ok -Detail $detail

# B. backend /photogallery/api/healthz
$r = Invoke-CurlCheck -Url "$BaseUrl/photogallery/api/healthz"
$ok = $r.Status -eq 200 -and -not (Test-LooksLikeSpaFallback -Body $r.Body)
$detail = if ($r.Error) { $r.Error }
          elseif ($r.Status -eq 200 -and (Test-LooksLikeSpaFallback -Body $r.Body)) {
              'HTTP 200 but body is SPA fallback HTML — FE proxy missing /photogallery/api/* (see #167)'
          }
          elseif ($r.Status -eq 0) { 'no response (is the stack up?)' }
          else { ('HTTP {0}' -f $r.Status) }
Add-Result -Name 'B. backend /photogallery/api/healthz'              -Passed $ok -Detail $detail

# C. config/public endpoint — epic's canonical AC
$r = Invoke-CurlCheck -Url "$BaseUrl/photogallery/api/config/public"
$hasGoogle = $r.Body -match 'googleClientId'
$ok = ($r.Status -eq 200) -and $hasGoogle -and -not (Test-LooksLikeSpaFallback -Body $r.Body)
$detail = if ($r.Error) { $r.Error }
          elseif (Test-LooksLikeSpaFallback -Body $r.Body) {
              'SPA fallback — FE proxy missing /photogallery/api/* (see #167)'
          }
          elseif ($r.Status -ne 200) { ('HTTP {0}' -f $r.Status) }
          elseif (-not $hasGoogle) { 'HTTP 200 but body missing "googleClientId"' }
          else { 'HTTP 200, JSON contains googleClientId' }
Add-Result -Name 'C. /photogallery/api/config/public has googleClientId' -Passed $ok -Detail $detail

# D. SignalR hub negotiate
$r = Invoke-CurlCheck -Url "$BaseUrl/photogallery/hubs/photo-progress/negotiate?negotiateVersion=1" -Method POST
$isSpa = Test-LooksLikeSpaFallback -Body $r.Body
# 200 (anonymous OK / DISABLE_AUTH) or 401 (auth required) both prove the
# request reached the hub. 404 / 502 / SPA fallback all fail.
$ok = ($r.Status -in 200, 401) -and -not $isSpa
$detail = if ($r.Error) { $r.Error }
          elseif ($isSpa) {
              'SPA fallback — FE proxy missing /photogallery/hubs/* (see #167)'
          }
          elseif ($r.Status -eq 404) {
              '404 — hub route not mounted; check S2 (UsePathBase) and S5 (nginx location /photogallery/hubs/)'
          }
          elseif ($r.Status -in 200, 401) {
              ('HTTP {0} (hub reachable)' -f $r.Status)
          }
          else { ('unexpected HTTP {0}' -f $r.Status) }
Add-Result -Name 'D. /photogallery/hubs/photo-progress/negotiate'    -Passed $ok -Detail $detail

# ---------- summary -----------------------------------------------------------

$total  = $script:Results.Count
$passed = ($script:Results | Where-Object { $_.Passed }).Count
$failed = $total - $passed

Write-Host ''
Write-Host ('Summary: {0}/{1} checks passed' -f $passed, $total) `
    -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })
Write-Host '(Browser smoke + SignalR upload are manual — see runbook §E and §F.)'

if ($failed -gt 0) {
    Write-Host ''
    Write-Host 'Failed checks:' -ForegroundColor Red
    $script:Results | Where-Object { -not $_.Passed } | ForEach-Object {
        Write-Host ('  - {0}: {1}' -f $_.Name, $_.Detail)
    }
    exit 1
}

exit 0
