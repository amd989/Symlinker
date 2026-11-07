<#
.SYNOPSIS
Sets "What's new" on the draft Store submission from listings.json, in every language.

.DESCRIPTION
Run after `msstore publish --noCommit` has made the draft. Reads the draft with
`msstore submission get`, sets each listing's release notes and sends it back with
`msstore submission update`.

listings.json's common.ReleaseNotesVersion says which version the notes were written for. When it
doesn't match -Version, the notes would describe an older release, so they are cleared instead and
a warning is printed.

.EXAMPLE
./packaging/msix/store/set-release-notes.ps1 -Version 4.3.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$ProductId = '9P8H93Z0TGK4',
    # For testing: read and write this file instead of calling msstore.
    [string]$SubmissionFile
)

$ErrorActionPreference = 'Stop'
$data = Get-Content (Join-Path $PSScriptRoot 'listings.json') -Raw -Encoding UTF8 | ConvertFrom-Json

# Finds a JSON property ignoring case: the Store API's casing isn't documented consistently.
function Get-Property([System.Text.Json.Nodes.JsonObject]$node, [string]$name) {
    foreach ($pair in $node) {
        if ($pair.Key -ieq $name) { return $pair }
    }
    return $null
}

function Set-ReleaseNotes([string]$submissionJson) {
    $current = $data.common.ReleaseNotesVersion -eq $Version
    if (-not $current) {
        Write-Host "::warning::listings.json has release notes for $($data.common.ReleaseNotesVersion), not $Version. Clearing What's new on the Store."
    }

    $submission = [System.Text.Json.Nodes.JsonNode]::Parse($submissionJson).AsObject()
    $listings = Get-Property $submission 'listings'
    if (-not $listings) { throw 'The submission has no listings.' }

    foreach ($listing in @($listings.Value.AsObject())) {
        $language = $listing.Key
        $base = Get-Property $listing.Value.AsObject() 'baseListing'
        if (-not $base) { throw "The $language listing has no baseListing." }

        $entry = $data.PSObject.Properties | Where-Object { $_.Name -ieq $language } | Select-Object -First 1
        if (-not $entry) { Write-Host "::warning::listings.json has no $language listing. Leaving it as is."; continue }

        $notes = if ($current) { [string]$entry.Value.ReleaseNotes } else { '' }
        $field = Get-Property $base.Value.AsObject() 'releaseNotes'
        $key = if ($field) { $field.Key } else { 'releaseNotes' }
        $base.Value.AsObject()[$key] = [System.Text.Json.Nodes.JsonValue]::Create($notes)
        Write-Host "What's new set for $language ($($notes.Length) characters)."
    }
    return $submission.ToJsonString()
}

if ($SubmissionFile) {
    $updated = Set-ReleaseNotes (Get-Content $SubmissionFile -Raw -Encoding UTF8)
    [IO.File]::WriteAllText($SubmissionFile, $updated)
    return
}

$output = (msstore submission get $ProductId) -join "`n"
if ($LASTEXITCODE -ne 0) { throw "msstore submission get failed ($LASTEXITCODE)." }
# Keep only the JSON, in case the CLI prints a notice before it.
$start = $output.IndexOf('{')
$end = $output.LastIndexOf('}')
if ($start -lt 0 -or $end -lt $start) { throw "msstore submission get returned no JSON: $output" }
$json = $output.Substring($start, $end - $start + 1)
$updated = Set-ReleaseNotes $json

# A file, not an argument: the whole submission is longer than a command line allows.
$file = Join-Path ([IO.Path]::GetTempPath()) "store-submission-$ProductId.json"
[IO.File]::WriteAllText($file, $updated)
try {
    msstore submission update $ProductId $file | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "msstore submission update failed ($LASTEXITCODE)." }
}
finally {
    Remove-Item $file -ErrorAction SilentlyContinue
}
