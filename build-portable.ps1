# Build portable ZIP for Scoop distribution

[CmdletBinding(PositionalBinding=$false)]
param (
    [string]$Version = ""
)

Set-StrictMode -version 2.0
$ErrorActionPreference = "Stop"

Write-Output "Building portable distribution for Scoop..."

# Determine version
if ([string]::IsNullOrEmpty($Version)) {
    $tag = $(git describe --tags 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Write-Error "No Git tag found. Please specify -Version or create a Git tag."
        exit 1
    }
    $Version = $tag.TrimStart('v')
    Write-Output "Detected version from Git tag: $Version"
} else {
    Write-Output "Using specified version: $Version"
}

$projDir = "Symlink Creator"
$outputDir = "bin/portable"
$publishDir = "$projDir/$outputDir"

# Clean output directory
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

# Build self-contained release
Write-Output "Building self-contained release..."
Push-Location $projDir
try {
    dotnet publish `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $outputDir `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
}
finally {
    Pop-Location
}

# Create ZIP file
$zipName = "symlinker-$Version-portable.zip"
$zipPath = "$zipName"

Write-Output "Creating ZIP archive: $zipPath"

# Remove old ZIP if exists
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Create ZIP (using .NET compression to avoid external dependencies)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    (Resolve-Path $publishDir).Path,
    (Join-Path (Get-Location) $zipPath),
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Output ("Created portable ZIP: {0:N2} MB" -f $zipSize)
Write-Output "Location: $(Resolve-Path $zipPath)"

# Calculate SHA256 hash for Scoop manifest
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
Write-Output ""
Write-Output "SHA256 hash for Scoop manifest:"
Write-Output $hash
