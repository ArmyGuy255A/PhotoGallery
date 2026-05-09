# add-issues-to-board.ps1
# Adds the 18 PhotoGallery v2 Phase 17+ issues to Project #3 and sets Status = Backlog.
#
# PREREQ: refresh gh auth with project scope first:
#   gh auth refresh -s project,read:org
#
# Then run:  pwsh ./scripts/add-issues-to-board.ps1

$ErrorActionPreference = 'Stop'
$owner   = 'ArmyGuy255A'
$repo    = 'ArmyGuy255A/PhotoGallery'
$projNum = 3
$issues  = 1..18

Write-Host "Resolving project metadata..."
$proj = gh project view $projNum --owner $owner --format json | ConvertFrom-Json
$projectId = $proj.id
Write-Host "Project ID: $projectId"

$fields = gh project field-list $projNum --owner $owner --format json | ConvertFrom-Json
$statusField = $fields.fields | Where-Object { $_.name -eq 'Status' } | Select-Object -First 1
if (-not $statusField) { throw "No Status field on project $projNum" }
$backlogOption = $statusField.options | Where-Object { $_.name -eq 'Backlog' } | Select-Object -First 1
if (-not $backlogOption) { throw "No 'Backlog' option in Status field" }
Write-Host "Status field: $($statusField.id)  Backlog option: $($backlogOption.id)"

foreach ($n in $issues) {
    $url = "https://github.com/$repo/issues/$n"
    Write-Host "Adding #$n -> $url"
    $added = gh project item-add $projNum --owner $owner --url $url --format json | ConvertFrom-Json
    $itemId = $added.id
    if (-not $itemId) { Write-Warning "No item id returned for #$n"; continue }

    gh project item-edit `
        --id $itemId `
        --project-id $projectId `
        --field-id $statusField.id `
        --single-select-option-id $backlogOption.id | Out-Null
    Write-Host "  OK #$n added and set to Backlog"
}

Write-Host "`nDone. All 18 issues added to Project #$projNum in Backlog."
