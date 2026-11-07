<#
.SYNOPSIS
Builds the Microsoft Store listing import: a CSV with every language's text and the screenshots.

.DESCRIPTION
Reads listings.json, renders 3 screenshots per language from the real main window (in that
language), and fills listing-template.csv (exported from Partner Center > Store listings >
Export listings). Output goes to Symlinker/bin/store-listing:

  listingData.csv           text and screenshot paths, for Import listings > Upload folder
  listingData-text-only.csv text only, for Import listings > Upload .csv
  <language>/store1-3.png   screenshots

.EXAMPLE
./packaging/msix/store/make-listings.ps1
#>
[CmdletBinding()]
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$storeDir = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $storeDir '../../..')
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'Symlinker/bin/store-listing' }

$data = Get-Content (Join-Path $storeDir 'listings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$template = Join-Path $storeDir 'listing-template.csv'

# Partner Center language column -> UI culture for the screenshots.
$cultures = [ordered]@{
    'en-us' = 'en-US'; 'de' = 'de-DE'; 'es' = 'es-ES'; 'fr' = 'fr-FR'; 'hi' = 'hi-IN'
    'it' = 'it-IT'; 'ja' = 'ja-JP'; 'pt' = 'pt-BR'; 'zh-hans' = 'zh-Hans'
}

# One filled-in form per screenshot, matching the order of each language's Captions.
$forms = @(
    @('--mode', 'folder', '--type', 'junction', '--location', '"D:\Games"', '--name', '"Steam Library"', '--target', '"E:\SteamLibrary"'),
    @('--mode', 'file', '--type', 'symbolic', '--location', '"C:\Users\Public\Documents"', '--name', '"settings.json"', '--target', '"D:\Dotfiles\settings.json"'),
    @('--mode', 'file', '--type', 'hard', '--location', '"D:\Photos\Favorites"', '--name', '"beach.jpg"', '--target', '"D:\Photos\2025\beach.jpg"')
)

# Screenshots
$toolOut = Join-Path $OutDir 'tool'
dotnet build (Join-Path $storeDir 'ScreenshotTool/ScreenshotTool.csproj') --configuration Release --output $toolOut --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Error 'Building the screenshot tool failed.' }
$tool = Join-Path $toolOut 'ScreenshotTool.exe'

foreach ($lang in $cultures.Keys) {
    $langDir = Join-Path $OutDir $lang
    New-Item -ItemType Directory -Force $langDir | Out-Null
    for ($i = 0; $i -lt $forms.Count; $i++) {
        $png = Join-Path $langDir "store$($i + 1).png"
        $toolArgs = @("`"$png`"", "`"$($data.$lang.Captions[$i])`"", $cultures[$lang]) + $forms[$i]
        $process = Start-Process $tool -ArgumentList $toolArgs -Wait -PassThru
        if ($process.ExitCode -ne 0 -or -not (Test-Path $png)) { Write-Error "Screenshot $($i + 1) failed for $lang." }
    }
    Write-Output "Screenshots: $lang"
}

# CSV
function Format-Cell([string]$value) {
    if ($null -eq $value) { return '' }
    $value = $value -replace "`r?`n", "`r`n"
    if ($value -match '[",\r\n]') { return '"' + ($value -replace '"', '""') + '"' }
    return $value
}

function Write-Listing([string]$path, [bool]$withScreenshots) {
    $lines = Get-Content $template -Encoding UTF8
    $header = $lines[0].Split(',')
    $out = New-Object System.Collections.Generic.List[string]
    $out.Add($lines[0])
    foreach ($line in $lines[1..($lines.Count - 1)]) {
        $cols = $line.Split(',')
        $field = $cols[0]
        if (-not $field) { $out.Add($line); continue }
        $row = @($cols[0], $cols[1], $cols[2], $cols[3])
        for ($c = 4; $c -lt $header.Count; $c++) {
            $lang = $header[$c]
            $l = $data.$lang
            $value = $cols[$c]
            switch -Regex ($field) {
                '^Title$' { $value = $data.common.Title }
                '^Description$' { $value = $l.Description }
                '^ReleaseNotes$' { $value = $l.ReleaseNotes }
                '^ShortDescription$' { $value = $l.ShortDescription }
                '^DevStudio$' { $value = $data.common.DevStudio }
                '^CopyrightTrademarkInformation$' { $value = $data.common.CopyrightTrademarkInformation }
                '^AdditionalLicenseTerms$' { $value = $data.common.AdditionalLicenseTerms }
                '^DesktopScreenshot([1-3])$' { if ($withScreenshots) { $value = "$lang/store$($Matches[1]).png" } }
                '^DesktopScreenshotCaption([1-3])$' { $value = $l.Captions[[int]$Matches[1] - 1] }
                '^Feature(\d+)$' { $n = [int]$Matches[1]; if ($n -le $l.Features.Count) { $value = $l.Features[$n - 1] } }
                '^SearchTerm(\d+)$' { $n = [int]$Matches[1]; if ($n -le $l.SearchTerms.Count) { $value = $l.SearchTerms[$n - 1] } }
            }
            $row += (Format-Cell $value)
        }
        $out.Add(($row -join ','))
    }
    # Partner Center exports UTF-8 with a BOM and CRLF; import the same way.
    [IO.File]::WriteAllText($path, ($out -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding $true))
}

Write-Listing (Join-Path $OutDir 'listingData.csv') $true
Write-Listing (Join-Path $OutDir 'listingData-text-only.csv') $false
Remove-Item $toolOut -Recurse -Force

Write-Output ""
Write-Output "Store listing written to $OutDir"
